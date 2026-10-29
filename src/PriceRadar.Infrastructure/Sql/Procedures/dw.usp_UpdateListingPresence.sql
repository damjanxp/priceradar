-- Sold heuristic (spec §7). For a complete run, every Active listing of the same source and scope that the run did not
-- see gets one more miss; at @MissedRunsThreshold it becomes Disappeared (a sold proxy, not proof of a sale).
-- Listings seen by the run were already reset by dw.usp_LoadRun.
-- Called by LoadJob in the same transaction that marks the run Loaded, so it runs exactly once per run.
CREATE OR ALTER PROCEDURE dw.usp_UpdateListingPresence
    @ScrapeRunId bigint,
    @MissedRunsThreshold tinyint = 2,
    @WindowDays int = 30
AS
BEGIN
    SET NOCOUNT, XACT_ABORT ON;

    DECLARE @SourceId tinyint, @ScopeKey varchar(200), @IsComplete bit, @RunAtUtc datetime2(3);
    SELECT @SourceId = SourceId, @ScopeKey = ScopeKey, @IsComplete = IsComplete,
           @RunAtUtc = COALESCE(StartedAtUtc, CreatedAtUtc)
    FROM ingest.ScrapeRun
    WHERE ScrapeRunId = @ScrapeRunId;

    -- Incomplete runs (page cap, errors) prove nothing about absent listings.
    IF @SourceId IS NULL OR @IsComplete = 0
        RETURN;

    UPDATE l
    SET l.MissedRunCount   = CASE WHEN l.MissedRunCount < 255 THEN l.MissedRunCount + 1 ELSE 255 END,
        l.Status           = CASE WHEN l.MissedRunCount + 1 >= @MissedRunsThreshold THEN 1 ELSE 0 END,
        -- A listing last seen outside the window disappears "stale": dated at its last sighting, so it does not
        -- count as sold in the last @WindowDays days.
        l.DisappearedAtUtc = CASE
                                 WHEN l.MissedRunCount + 1 < @MissedRunsThreshold THEN NULL
                                 WHEN l.LastSeenAtUtc < DATEADD(day, -@WindowDays, @RunAtUtc) THEN l.LastSeenAtUtc
                                 ELSE @RunAtUtc
                             END
    FROM dw.Listing AS l
    WHERE l.SourceId = @SourceId
      AND l.ScopeKey = @ScopeKey
      AND l.Status = 0
      AND l.LastSeenRunId < @ScrapeRunId;
END
