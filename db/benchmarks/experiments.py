"""DAY-6 experiments that need more than "run the query 5 times" (spec §11.4 b–d).

    python db/benchmarks/experiments.py sniffing            # runs 02-parameter-sniffing.sql (plan-cache DMVs)
    python db/benchmarks/experiments.py columnstore         # Q1-Q3 with a full nonclustered columnstore index, then drop it
    python db/benchmarks/experiments.py columnstore-empty   # same with an empty filtered columnstore index (batch mode only)
    python db/benchmarks/experiments.py querystore    # enable Query Store and list the top queries

Results are printed and written to docs/benchmarks/results/<experiment>.json. Local container only: the
experiments flush the plan cache.
"""
from __future__ import annotations

import json
import re
import statistics
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from run_benchmarks import NS, OUT, measure, sqlcmd  # noqa: E402

STATS = "EXEC dw.usp_GetBookStats @BookId = {book}, @WindowDays = 30, @AsOfUtc = '2026-10-28T12:00:00';"
HOT, COLD = 6442, 13212


def plan_xml(sql: str) -> str:
    out = sqlcmd(f"SET NOCOUNT ON; SET STATISTICS XML ON;\n{sql}\nSET STATISTICS XML OFF;")
    docs = re.findall(r"<ShowPlanXML.*?</ShowPlanXML>", out, flags=re.S)
    return next((d for d in docs if "[PriceSnapshot]" in d), docs[0])


def plan_facts(xml: str) -> dict:
    """Memory grant, estimated vs actual rows on PriceSnapshot, spills and batch-mode operators of an actual plan."""
    root = ET.fromstring(xml)
    grant = root.find(".//p:MemoryGrantInfo", NS)
    facts = {
        "grantedKb": int(grant.get("GrantedMemory", 0)) if grant is not None else 0,
        "usedKb": int(grant.get("MaxUsedMemory", 0)) if grant is not None else 0,
        "spills": len(root.findall(".//p:SpillToTempDb", NS)),
        "batchModeOps": sorted({r.get("PhysicalOp") for r in root.iter(f"{{{NS['p']}}}RelOp")
                                if r.get("EstimatedExecutionMode") == "Batch"}),
    }
    for relop in root.iter(f"{{{NS['p']}}}RelOp"):
        obj = relop.find("./*/p:Object", NS)
        if obj is not None and obj.get("Table") == "[PriceSnapshot]":
            actual = sum(int(t.get("ActualRows", 0)) for t in relop.findall(".//p:RunTimeCountersPerThread", NS))
            facts.update(estimatedRows=float(relop.get("EstimateRows")), actualRows=actual, op=relop.get("PhysicalOp"))
            break
    return facts


def medians(sql: str, runs: int = 5) -> dict:
    samples = [measure(sql) for _ in range(runs)]
    return {k: statistics.median(s[k] for s in samples) for k in samples[0]}


def sniffing() -> dict:
    """Plan reuse verified through sys.dm_exec_query_stats (not by re-capturing plans, which recompiles them)."""
    script = (Path(__file__).with_name("02-parameter-sniffing.sql")).read_text(encoding="utf-8")
    out = sqlcmd(script)
    return {"output": [l.rstrip() for l in out.splitlines() if l.strip() and not l.startswith("Table ")]}


INSERT_10K = """SET NOCOUNT ON;
BEGIN TRAN;
INSERT INTO ingest.ScrapeRun (SourceId, ScopeKey, Status, Stage, IsComplete, MaxPages, PagesFetched, ListingsFetched,
    ListingsRejected, ListingsLoaded, AiCalls, EurRsdRate, IdempotencyKey, CreatedAtUtc)
VALUES (2, 'synthetic:category:knjige', 2, 4, 1, 10, 10, 0, 0, 0, 0, 117.2, NEWID(), SYSUTCDATETIME());
DECLARE @run bigint = SCOPE_IDENTITY();
SET STATISTICS TIME ON;
INSERT INTO dw.PriceSnapshot (ListingId, BookId, SourceId, ScrapeRunId, DateKey, PriceRsd, CapturedAtUtc)
SELECT TOP (10000) ListingId, BookId, SourceId, @run, 20261028, CurrentPriceRsd, SYSUTCDATETIME() FROM dw.Listing ORDER BY ListingId;
SET STATISTICS TIME OFF;
ROLLBACK;"""


def insert_cost_ms(runs: int = 3) -> float:
    """Elapsed time to insert one run's worth (10,000 rows) of snapshots; rolled back, so the data is unchanged."""
    times = []
    for _ in range(runs):
        found = re.findall(r"elapsed time = (\d+) ms", sqlcmd(INSERT_10K))
        times.append(int(found[-1]))
    return statistics.median(times)


FULL_NCCI = ("CREATE NONCLUSTERED COLUMNSTORE INDEX NCCI_PriceSnapshot_Experiment ON dw.PriceSnapshot "
             "(BookId, DateKey, ListingId, PriceRsd, CapturedAtUtc) WITH (MAXDOP = 1);")
# Holds no rows (contradictory filter), costs nothing to maintain, but its existence lets the optimiser use batch mode.
EMPTY_NCCI = ("CREATE NONCLUSTERED COLUMNSTORE INDEX NCCI_PriceSnapshot_Experiment ON dw.PriceSnapshot (PriceSnapshotId) "
              "WHERE PriceSnapshotId = -1 AND PriceSnapshotId = -2;")


def columnstore(create: str = FULL_NCCI) -> dict:
    queries = {
        "Q1": STATS.format(book=HOT),
        "Q2": STATS.format(book=COLD),
        "Q3": "EXEC dw.usp_GetPriceHistory @BookId = 6442, @FromDateKey = 20260501, @ToDateKey = 20261028, @Bucket = 'week';",
    }
    result = {"index": create, "insert10kMs_rowstoreOnly": insert_cost_ms()}
    timing = sqlcmd(f"SET NOCOUNT ON; SET STATISTICS TIME ON;\n{create}\nSET STATISTICS TIME OFF;")
    build = re.findall(r"elapsed time = (\d+) ms", timing)
    result["buildMs"] = int(build[-1]) if build else None
    try:
        result["insert10kMs_withColumnstore"] = insert_cost_ms()
        for name, sql in queries.items():
            sqlcmd("DBCC FREEPROCCACHE WITH NO_INFOMSGS;")
            measure(sql)
            result[name] = {**medians(sql), **plan_facts(plan_xml(sql))}
    finally:
        sqlcmd("DROP INDEX IF EXISTS NCCI_PriceSnapshot_Experiment ON dw.PriceSnapshot;")
        sqlcmd("DBCC FREEPROCCACHE WITH NO_INFOMSGS;")
    return result


def querystore() -> dict:
    sqlcmd("ALTER DATABASE PriceRadar SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO, "
           "INTERVAL_LENGTH_MINUTES = 15);")
    for _ in range(5):
        for book in (HOT, COLD):
            sqlcmd(STATS.format(book=book))
    sqlcmd("EXEC sys.sp_query_store_flush_db;")
    out = sqlcmd("""SET NOCOUNT ON;
        SELECT TOP 8 CONCAT(ISNULL(OBJECT_NAME(q.object_id), 'ad hoc'), ' | ', LEFT(REPLACE(REPLACE(qt.query_sql_text, CHAR(13), ' '), CHAR(10), ' '), 70),
               ' | execs=', SUM(rs.count_executions), ' | avg_ms=', CAST(SUM(rs.avg_duration * rs.count_executions) / SUM(rs.count_executions) / 1000.0 AS decimal(9,2)),
               ' | avg_reads=', CAST(SUM(rs.avg_logical_io_reads * rs.count_executions) / SUM(rs.count_executions) AS int),
               ' | plans=', COUNT(DISTINCT p.plan_id))
        FROM sys.query_store_runtime_stats rs
        JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id
        JOIN sys.query_store_query q ON q.query_id = p.query_id
        JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
        WHERE q.object_id IN (OBJECT_ID('dw.usp_GetBookStats'), OBJECT_ID('dw.usp_GetPriceHistory'))
        GROUP BY q.object_id, qt.query_sql_text
        ORDER BY SUM(rs.avg_duration * rs.count_executions) DESC;""")
    rows = [l.strip() for l in out.splitlines() if "|" in l]
    return {"topQueries": rows}


def main() -> None:
    name = sys.argv[1]
    result = {"sniffing": sniffing, "columnstore": columnstore, "columnstore-empty": lambda: columnstore(EMPTY_NCCI),
              "querystore": querystore}[name]()
    print(json.dumps(result, indent=2))
    (OUT / "results" / f"{name}.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
