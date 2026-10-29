-- Price distribution and supply for one book (spec §5.3, FR-13).
-- Result set 1: statistics over the LATEST price of each listing seen in the window, so a listing observed in many runs
--               counts once (D-21). Exact PERCENTILE_CONT: per-book samples are small and the values are testable.
-- Result set 2: current supply and the disappeared-in-window count (the sold proxy, spec §7).
-- @AsOfUtc makes the window reproducible in tests; it defaults to now.
CREATE OR ALTER PROCEDURE dw.usp_GetBookStats
    @BookId int,
    @WindowDays int = 30,
    @AsOfUtc datetime2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @now datetime2(3) = COALESCE(@AsOfUtc, SYSUTCDATETIME());
    DECLARE @from datetime2(3) = DATEADD(day, -@WindowDays, @now);
    DECLARE @fromKey int = CONVERT(int, CONVERT(char(8), @from, 112));
    DECLARE @toKey int = CONVERT(int, CONVERT(char(8), @now, 112));

    WITH latest AS (
        SELECT ps.PriceRsd,
               ROW_NUMBER() OVER (PARTITION BY ps.ListingId ORDER BY ps.CapturedAtUtc DESC, ps.PriceSnapshotId DESC) AS rn
        FROM dw.PriceSnapshot AS ps
        WHERE ps.BookId = @BookId
          AND ps.DateKey BETWEEN @fromKey AND @toKey
          AND ps.CapturedAtUtc > @from
          AND ps.CapturedAtUtc <= @now
    )
    SELECT TOP (1)
        COUNT(*)      OVER () AS SampleSize,
        MIN(PriceRsd) OVER () AS MinPrice,
        MAX(PriceRsd) OVER () AS MaxPrice,
        CAST(PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY PriceRsd) OVER () AS decimal(12,2)) AS P25,
        CAST(PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY PriceRsd) OVER () AS decimal(12,2)) AS Median,
        CAST(PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY PriceRsd) OVER () AS decimal(12,2)) AS P75
    FROM latest
    WHERE rn = 1;

    SELECT COALESCE(SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END), 0) AS ActiveListings,
           COALESCE(SUM(CASE WHEN Status = 1 AND DisappearedAtUtc > @from AND DisappearedAtUtc <= @now THEN 1 ELSE 0 END), 0)
               AS DisappearedInWindow
    FROM dw.Listing
    WHERE BookId = @BookId;
END
