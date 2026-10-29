-- Price series for one book (spec §5.3, FR-14): min / median / max / count of snapshots per day or ISO week.
-- Weeks are identified by their Monday, taken from dw.DimDate (DayOfWeek: 1 = Monday).
CREATE OR ALTER PROCEDURE dw.usp_GetPriceHistory
    @BookId int,
    @FromDateKey int,
    @ToDateKey int,
    @Bucket varchar(4) = 'day'
AS
BEGIN
    SET NOCOUNT ON;

    IF @Bucket NOT IN ('day', 'week')
        THROW 50002, 'dw.usp_GetPriceHistory: @Bucket must be ''day'' or ''week''.', 1;

    WITH s AS (
        SELECT CASE WHEN @Bucket = 'week' THEN DATEADD(day, 1 - d.[DayOfWeek], d.[Date]) ELSE d.[Date] END AS Period,
               ps.PriceRsd
        FROM dw.PriceSnapshot AS ps
        JOIN dw.DimDate AS d ON d.DateKey = ps.DateKey
        WHERE ps.BookId = @BookId
          AND ps.DateKey BETWEEN @FromDateKey AND @ToDateKey
    )
    SELECT DISTINCT
        Period,
        MIN(PriceRsd) OVER (PARTITION BY Period) AS MinPrice,
        CAST(PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY PriceRsd) OVER (PARTITION BY Period) AS decimal(12,2)) AS MedianPrice,
        MAX(PriceRsd) OVER (PARTITION BY Period) AS MaxPrice,
        COUNT(*) OVER (PARTITION BY Period) AS Snapshots
    FROM s
    ORDER BY Period;
END
