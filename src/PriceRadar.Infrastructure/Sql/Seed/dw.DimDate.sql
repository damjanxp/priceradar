-- Populates dw.DimDate for 2024-01-01 .. 2030-12-31 (2,557 rows). Idempotent.
-- DayOfWeek: 1 = Monday .. 7 = Sunday, independent of the server's @@DATEFIRST setting.
WITH n AS (
    SELECT TOP (DATEDIFF(day, '2024-01-01', '2030-12-31') + 1)
           ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
    FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b
),
d AS (
    SELECT DATEADD(day, i, CAST('2024-01-01' AS date)) AS dt FROM n
),
k AS (
    SELECT dt, ((DATEPART(weekday, dt) + @@DATEFIRST - 2) % 7) + 1 AS dow FROM d
)
INSERT INTO dw.DimDate (DateKey, [Date], [Year], [Month], [Day], IsoWeek, [DayOfWeek], IsWeekend)
SELECT CONVERT(int, CONVERT(char(8), k.dt, 112)),
       k.dt,
       YEAR(k.dt),
       MONTH(k.dt),
       DAY(k.dt),
       DATEPART(iso_week, k.dt),
       k.dow,
       CASE WHEN k.dow >= 6 THEN 1 ELSE 0 END
FROM k
WHERE NOT EXISTS (SELECT 1 FROM dw.DimDate AS x WHERE x.[Date] = k.dt);
