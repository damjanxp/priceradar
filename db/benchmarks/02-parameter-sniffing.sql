-- DAY-6 parameter-sniffing check through the plan cache (BENCHMARKS.md, experiment b). Local container only.
-- Compiles the stats plan for a small book, reuses it for the hot book 6442, then compares with the hot book's own plan.
SET NOCOUNT ON;
-- a small book that still has rows in the window
DECLARE @small int = (SELECT TOP 1 BookId FROM dw.PriceSnapshot WHERE DateKey >= 20260928 GROUP BY BookId HAVING COUNT(*) BETWEEN 8 AND 12 ORDER BY BookId);
PRINT CONCAT('small book: ', @small);

DBCC FREEPROCCACHE WITH NO_INFOMSGS;
EXEC dw.usp_GetBookStats @BookId = @small, @WindowDays = 30, @AsOfUtc = '2026-10-28T12:00:00';   -- compiles for the small book
SET STATISTICS IO, TIME ON;
EXEC dw.usp_GetBookStats @BookId = 6442, @WindowDays = 30, @AsOfUtc = '2026-10-28T12:00:00';     -- hot book reuses it
SET STATISTICS IO, TIME OFF;

PRINT '--- cached plan after small-then-hot';
SELECT qs.execution_count AS execs, qs.last_grant_kb, qs.last_used_grant_kb, qs.last_spills, qs.last_logical_reads, qs.last_elapsed_time / 1000.0 AS last_ms,
       qp.query_plan.value('declare namespace p="http://schemas.microsoft.com/sqlserver/2004/07/showplan";
                            (//p:RelOp[p:IndexScan/p:Object/@Table="[PriceSnapshot]"]/@EstimateRows)[1]', 'float') AS est_rows_compiled_for
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
WHERE st.objectid = OBJECT_ID('dw.usp_GetBookStats') AND st.text LIKE '%ROW_NUMBER%' AND qs.statement_start_offset > 0
  AND SUBSTRING(st.text, qs.statement_start_offset / 2 + 1, 40) LIKE '%WITH latest%';

DBCC FREEPROCCACHE WITH NO_INFOMSGS;
EXEC dw.usp_GetBookStats @BookId = 6442, @WindowDays = 30, @AsOfUtc = '2026-10-28T12:00:00';
PRINT '--- hot book with its own plan';
SELECT qs.execution_count AS execs, qs.last_grant_kb, qs.last_used_grant_kb, qs.last_spills, qs.last_logical_reads, qs.last_elapsed_time / 1000.0 AS last_ms
FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE st.objectid = OBJECT_ID('dw.usp_GetBookStats') AND SUBSTRING(st.text, qs.statement_start_offset / 2 + 1, 40) LIKE '%WITH latest%';
