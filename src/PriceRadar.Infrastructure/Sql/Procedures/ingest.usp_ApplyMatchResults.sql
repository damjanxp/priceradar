-- Writes match decisions back to staging. NewBook decisions create their canonical book here (once per NormKey),
-- so every row leaves this procedure with a BookId unless it is Pending or Rejected. Safe to call repeatedly.
CREATE OR ALTER PROCEDURE ingest.usp_ApplyMatchResults
    @ScrapeRunId bigint,
    @Results ingest.MatchResultType READONLY
AS
BEGIN
    SET NOCOUNT, XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- 1. New canonical books. UPDLOCK/HOLDLOCK serialises concurrent creators of the same key;
    --    UX_Book_NormKey is the final guard.
    WITH candidates AS (
        SELECT r.NormTitle, r.NormAuthor, r.NewBookTitle, r.NewBookAuthor,
               COALESCE(r.NewBookOrigin, 1) AS Origin, COALESCE(r.NewBookNeedsReview, 0) AS NeedsReview,
               ROW_NUMBER() OVER (PARTITION BY r.NormAuthor, r.NormTitle ORDER BY r.StagingListingId) AS rn
        FROM @Results AS r
        WHERE r.MatchStatus = 3 AND r.ProcessStatus = 1 AND r.BookId IS NULL
    )
    INSERT INTO dw.Book (Title, Author, NormTitle, NormAuthor, Origin, NeedsReview, CreatedAtUtc)
    SELECT c.NewBookTitle, c.NewBookAuthor, c.NormTitle, c.NormAuthor, c.Origin, c.NeedsReview, SYSUTCDATETIME()
    FROM candidates AS c
    WHERE c.rn = 1
      AND NOT EXISTS (SELECT 1 FROM dw.Book AS b WITH (UPDLOCK, HOLDLOCK)
                      WHERE b.NormKey = c.NormAuthor + '|' + c.NormTitle);

    -- 2. Write back. NewBook rows pick up the BookId of the book created above (or by an earlier run).
    UPDATE s
    SET s.PriceRsd      = r.PriceRsd,
        s.NormTitle     = r.NormTitle,
        s.NormAuthor    = r.NormAuthor,
        s.BookId        = COALESCE(r.BookId, b.BookId),
        s.MatchStatus   = r.MatchStatus,
        s.MatchScore    = r.MatchScore,
        s.ProcessStatus = r.ProcessStatus,
        s.RejectReason  = r.RejectReason
    FROM ingest.StagingListing AS s
    JOIN @Results AS r ON r.StagingListingId = s.StagingListingId
    LEFT JOIN dw.Book AS b
           ON r.BookId IS NULL AND r.MatchStatus = 3 AND b.NormKey = r.NormAuthor + '|' + r.NormTitle
    WHERE s.ScrapeRunId = @ScrapeRunId;

    COMMIT TRANSACTION;
END
