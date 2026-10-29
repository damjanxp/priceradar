-- DAY-4 baseline (spec §11.4): run in SSMS / Azure Data Studio with "Include Actual Execution Plan" (Ctrl+M).
-- Hot book 6442 (8,275 snapshots), median book 13212 (14 snapshots) from `seed --seed 42 --end-date 2026-10-28`.
-- The automated version (1 warm-up + 5 runs, medians, saved .sqlplan files) is db/benchmarks/run_benchmarks.py.
USE PriceRadar;
GO
SELECT name, type_desc FROM sys.indexes WHERE object_id = OBJECT_ID('dw.PriceSnapshot');   -- baseline: PK + UX only
GO
SET STATISTICS IO, TIME ON;
GO
-- Q1: stats, hot book
EXEC dw.usp_GetBookStats @BookId = 6442, @WindowDays = 30, @AsOfUtc = '2026-10-28T12:00:00';
GO
-- Q2: stats, median ("cold") book
EXEC dw.usp_GetBookStats @BookId = 13212, @WindowDays = 30, @AsOfUtc = '2026-10-28T12:00:00';
GO
-- Q3: price history, hot book, 180 days by ISO week
EXEC dw.usp_GetPriceHistory @BookId = 6442, @FromDateKey = 20260501, @ToDateKey = 20261028, @Bucket = 'week';
GO
-- Q4: lookup exact-key search
SELECT BookId FROM dw.Book WHERE NormKey = 'andric ivo|na drini cuprija';
GO
SET STATISTICS IO, TIME OFF;
GO
-- Optional cold-cache variant (local container only, never on a shared server):
-- CHECKPOINT; DBCC DROPCLEANBUFFERS;
