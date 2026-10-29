-- Loads one run's normalised staging rows into the warehouse (spec §4.2 step 3, FR-10).
-- Idempotent: running it twice for the same run changes nothing the second time.
CREATE OR ALTER PROCEDURE dw.usp_LoadRun
    @ScrapeRunId bigint
AS
BEGIN
    SET NOCOUNT, XACT_ABORT ON;

    DECLARE @SourceId tinyint, @ScopeKey varchar(200);
    SELECT @SourceId = SourceId, @ScopeKey = ScopeKey FROM ingest.ScrapeRun WHERE ScrapeRunId = @ScrapeRunId;
    IF @SourceId IS NULL
        THROW 50001, 'dw.usp_LoadRun: unknown ScrapeRunId.', 1;

    BEGIN TRANSACTION;

    -- 1. Listings: insert new ads, refresh the ones seen before. Rows of an older run never overwrite a newer one.
    MERGE dw.Listing WITH (HOLDLOCK) AS t
    USING (
        SELECT s.ExternalId, s.RawTitle, s.RawCondition, s.Url, s.PriceRsd, s.BookId, s.MatchStatus, s.MatchScore,
               s.ScrapedAtUtc
        FROM ingest.StagingListing AS s
        WHERE s.ScrapeRunId = @ScrapeRunId AND s.ProcessStatus IN (1, 2)   -- 2 = loaded by an earlier attempt
    ) AS src
    ON t.SourceId = @SourceId AND t.ExternalId = src.ExternalId
    WHEN MATCHED AND t.LastSeenRunId <= @ScrapeRunId THEN UPDATE SET
        t.BookId           = CASE WHEN src.MatchStatus = 0 THEN t.BookId ELSE src.BookId END,
        t.MatchStatus      = CASE WHEN src.MatchStatus = 0 AND t.BookId IS NOT NULL THEN t.MatchStatus ELSE src.MatchStatus END,
        t.MatchScore       = CASE WHEN src.MatchStatus = 0 AND t.BookId IS NOT NULL THEN t.MatchScore ELSE src.MatchScore END,
        t.Title            = src.RawTitle,
        t.Condition        = src.RawCondition,
        t.Url              = src.Url,
        t.CurrentPriceRsd  = src.PriceRsd,
        t.LastSeenAtUtc    = src.ScrapedAtUtc,
        t.LastSeenRunId    = @ScrapeRunId,
        t.MissedRunCount   = 0,
        t.Status           = 0,             -- seen again: a Disappeared listing was not sold after all
        t.DisappearedAtUtc = NULL
    WHEN NOT MATCHED BY TARGET THEN INSERT
        (SourceId, ExternalId, BookId, MatchStatus, MatchScore, Title, Condition, Url, ScopeKey, Status,
         CurrentPriceRsd, FirstSeenAtUtc, LastSeenAtUtc, LastSeenRunId, MissedRunCount)
    VALUES
        (@SourceId, src.ExternalId, src.BookId, src.MatchStatus, src.MatchScore, src.RawTitle, src.RawCondition, src.Url,
         @ScopeKey, 0, src.PriceRsd, src.ScrapedAtUtc, src.ScrapedAtUtc, @ScrapeRunId, 0);

    -- 2. Fact rows: one snapshot per listing per run (UX_PriceSnapshot_Listing_Run is the hard guarantee).
    INSERT INTO dw.PriceSnapshot (ListingId, BookId, SourceId, ScrapeRunId, DateKey, PriceRsd, CapturedAtUtc)
    SELECT l.ListingId, l.BookId, @SourceId, @ScrapeRunId,
           CONVERT(int, CONVERT(char(8), s.ScrapedAtUtc, 112)), s.PriceRsd, s.ScrapedAtUtc
    FROM ingest.StagingListing AS s
    JOIN dw.Listing AS l ON l.SourceId = @SourceId AND l.ExternalId = s.ExternalId
    WHERE s.ScrapeRunId = @ScrapeRunId AND s.ProcessStatus IN (1, 2)
      AND NOT EXISTS (SELECT 1 FROM dw.PriceSnapshot AS p WHERE p.ListingId = l.ListingId AND p.ScrapeRunId = @ScrapeRunId);

    -- 3. Mark staging rows loaded and record the counters on the run.
    UPDATE ingest.StagingListing SET ProcessStatus = 2 WHERE ScrapeRunId = @ScrapeRunId AND ProcessStatus = 1;

    UPDATE r
    SET r.ListingsLoaded   = (SELECT COUNT(*) FROM ingest.StagingListing WHERE ScrapeRunId = @ScrapeRunId AND ProcessStatus = 2),
        r.ListingsRejected = (SELECT COUNT(*) FROM ingest.StagingListing WHERE ScrapeRunId = @ScrapeRunId AND ProcessStatus = 3)
    FROM ingest.ScrapeRun AS r
    WHERE r.ScrapeRunId = @ScrapeRunId;

    COMMIT TRANSACTION;
END
