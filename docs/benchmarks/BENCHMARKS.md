# PriceRadar benchmarks

Method: spec §11.4–11.5. Numbers are only comparable within one environment; never across machines.

## Environment

| Item | Value |
|---|---|
| Machine | Laptop, 13th Gen Intel Core i5-13420H, 16 GB RAM, Windows 11 Home |
| Docker | Docker Desktop 29.3.1 (WSL2 VM: 12 vCPUs, 7.6 GB), SQL container `mem_limit: 2g` |
| SQL Server | 16.0.4305.5 (2022 CU27), Developer Edition, image `mcr.microsoft.com/mssql/server:2022-latest` |
| .NET | SDK 9.0.309, Release build |

## Synthetic dataset (DAY-2)

Command:

```powershell
dotnet run --project tools/PriceRadar.Seeder -c Release -- seed --seed 42 --end-date 2026-10-27 --reset
```

Shape (deterministic for `--seed 42`; verified by running the command twice and comparing):

| Table | Rows |
|---|---|
| `ingest.ScrapeRun` (source `synthetic`) | 180 (one per day, 2026-05-01 … 2026-10-27) |
| `dw.Book` | 20,000 |
| `dw.Listing` | 60,000 (3,059 Active, 56,941 Disappeared) |
| `dw.PriceSnapshot` | 500,000 |

Popularity skew (Zipf–Mandelbrot, weight `1 / (rank + 10)^1.1`, random rank order):

| | BookId | Snapshots |
|---|---|---|
| Hottest book (Q1 "hot" in §11.4) | 6442 | 8,275 |
| 2nd–5th | 2201, 13873, 17304, 2144 | 8,242 · 7,331 · 6,514 · 5,941 |
| Median book (Q2 "cold") | 13212 | 14 |

A plain Zipf (`q = 0`) was tried first: it put 72,843 snapshots (15 % of the table) on one book, which no real market resembles, so the offset `q = 10` was added.

### Load timings (SqlBulkCopy, `TableLock | CheckConstraints | KeepIdentity`, batch 10,000, streaming reader)

| Run | Reset | Runs | Books | Listings | Snapshots | Total |
|---|---|---|---|---|---|---|
| 1 (empty tables) | — | 0.2 s | 0.7 s | 1.5 s | 7.8 s (64k rows/s) | 10.5 s |
| 2 (`--reset`) | 6.3 s | 0.1 s | 0.7 s | 3.6 s | 8.3 s (60k rows/s) | 19.8 s |
| 3 (`--reset`) | 12.9 s | 0.1 s | 1.7 s | 5.4 s | 10.0 s (50k rows/s) | 30.8 s |

NFR-3 target (500k snapshots in < 60 s): **met** — 7.8–10.0 s for the snapshot table, ≤ 31 s end-to-end including a full reset.

`CheckConstraints` is deliberate: without it SQL Server marks the foreign keys as *not trusted* after a bulk load, and the optimiser stops using them. Verified after seeding: `SELECT name FROM sys.foreign_keys WHERE is_not_trusted = 1` returns no rows.

## Query benchmarks

Method: `python db/benchmarks/run_benchmarks.py <label>` runs each query once to warm up, then 5 times with
`SET STATISTICS IO, TIME ON`, and reports medians; it also saves the actual plan per query in `plans/`. Warm buffer pool.
Raw samples: `results/<label>.json`. Dataset: `seed --seed 42 --end-date 2026-10-28` plus fixture/KP runs (500,425 snapshots).
Manual version for SSMS/ADS: [`db/benchmarks/01-baseline.sql`](../../db/benchmarks/01-baseline.sql).

| # | Query | Variant | Logical reads (PriceSnapshot) | Logical reads (total) | CPU ms | Elapsed ms | Plan shape | Note |
|---|---|---|---|---|---|---|---|---|
| 1 | Q1 stats, hot book 6442 | **Baseline** (PK only) | 4,736 | 6,809 | 69 | 15 | Clustered Index Scan (parallel, 12 threads) | Whole table read to find 8,275 rows |
| 2 | Q2 stats, median book 13212 | **Baseline** | 4,736 | 4,740 | 63 | 12 | Clustered Index Scan (parallel) | Same full scan for 14 rows |
| 3 | Q3 history, hot book, 180 days by week | **Baseline** | 4,736 | 55,457 | 136 | 39 | Clustered Index Scan → window spool | 50,718 reads are the **Worktable** of `PERCENTILE_CONT … OVER (PARTITION BY week)` |
| 4 | Q4 lookup exact-key search | **Baseline** | — | 3 | 0 | 0 | Index Seek `UX_Book_NormKey` | Already optimal |
| 5 | Q1 stats, hot book | + `IX_PriceSnapshot_Book_Date` | **12** | 2,085 | 9 | 10 | Index Seek → Sort → window spool | 2,038 of the reads are now the worktable |
| 6 | Q2 stats, median book | + covering index | **3** | 6 | 5 | 6 | Index Seek | |
| 7 | Q3 history | + covering index | 51 | 50,742 | 40 | 41 | Index Seek → window spool | Scan gone, **spool unchanged**, so elapsed time unchanged |
| 8 | Q1 stats, hot book | + empty columnstore index (**final**) | 14 | **49** | 4 | **5** | Index Seek, batch-mode Window Aggregate | No worktable |
| 9 | Q2 stats, median book | + empty columnstore index (**final**) | 4 | **8** | 1 | **1** | Index Seek, batch mode | |
| 10 | Q3 history | + empty columnstore index (**final**) | 53 | **56** | 8 | **9** | Index Seek, batch-mode Window Aggregate + Adaptive Join | 50,742 → 56 reads |

### Before / after summary (DAY-6)

| Query | Baseline (total reads · elapsed) | Final (total reads · elapsed) | Reads | Elapsed |
|---|---|---|---|---|
| Q1 stats, hot book (8,275 snapshots) | 6,809 · 21 ms | 49 · 5 ms | **−99.3 %** | **4.2× faster** |
| Q2 stats, median book (14 snapshots) | 4,495 · 37 ms | 8 · 1 ms | −99.8 % | 37× faster |
| Q3 price history, 180 days by week | 55,457 · 40 ms | 56 · 9 ms | −99.9 % | 4.4× faster |
| `GET /api/lookup` under load (k6, 10 VUs, 60 s) | p95 **145.9 ms**, 101 req/s | p95 **18.5 ms**, 838 req/s | — | **7.9× faster p95, 8.3× throughput** |

Baselines are the DAY-6 re-run (`results/baseline-day6.json`) on the same dataset as DAY-4. NFR-1 (lookup p95 < 200 ms) and NFR-2 (hot-book stats < 1,000 logical reads and < 50 ms) are both met.

### Experiments

**(a) Covering index `IX_PriceSnapshot_Book_Date (BookId, DateKey) INCLUDE (ListingId, PriceRsd, CapturedAtUtc)`.** Kept. It turns every book query from a parallel full scan into a seek (PriceSnapshot reads 4,736 → 3–51). It does not touch the window spool, so Q3's elapsed time stayed at 41 ms.

**(b) Parameter sniffing.** No action needed. Checked through the plan cache (`sys.dm_exec_query_stats`), not just by timing: after `DBCC FREEPROCCACHE`, a small book (8 rows in the window) compiled the plan with an estimate of 8.7 rows, and the hot book (1,405 rows) then reused it (`execution_count = 2`). It ran with a 1,024 KB memory grant (192 KB used), 0 spills and the same 2,105 reads and 2.3 ms as with its own plan (1,488 KB grant). With the covering index the plan shape does not depend on the parameter, so `OPTION (RECOMPILE)` would only add compile cost to every lookup. Re-checked on the final index set (batch mode) with [`02-parameter-sniffing.sql`](../../db/benchmarks/02-parameter-sniffing.sql): the reused plan reads 14 pages, gets a 2,560 KB grant (2,088 KB used) and has 0 spills. Before the index the shape *did* vary: Query Store shows a parallel scan and a serial scan with a `MemoryGrantWarning` for the same statement.

**(c) Columnstore for the window spool.** Two variants, measured with `db/benchmarks/experiments.py columnstore` and `columnstore-empty` (`results/columnstore*.json`):

| | Full NCCI `(BookId, DateKey, ListingId, PriceRsd, CapturedAtUtc)` | **Empty filtered NCCI** `(PriceSnapshotId) WHERE PriceSnapshotId = -1 AND PriceSnapshotId = -2` |
|---|---|---|
| Build | 2,517 ms (`MAXDOP 1`) | **29 ms** |
| Insert 10,000 snapshots (rolled back) | 95 → 123 ms (+29 %) | 120 → 118 ms (no change) |
| Q1 / Q3 total reads | 52 / 59 | 55 / 56 |
| Q1 / Q3 elapsed | 3 / 11 ms | 6 / 10 ms |

Neither plan reads the columnstore index. Its existence is what lets the optimiser run the rowstore seek in **batch mode**, and the batch-mode Window Aggregate needs no worktable. The empty index gives the same gain with no maintenance cost, so it was adopted (migration `AddBatchModeColumnstore`, D-40).

*Production-debugging note:* the first attempt at the full index hung for 10+ minutes on wait type `RESOURCE_SEMAPHORE`. `sys.dm_exec_query_memory_grants` showed it requesting 150 MB (also its minimum) while the semaphore target was 128 MB, so the grant could never be given. The 2 GB container's memory was held by a 420 MB log-pool cache after the bulk loads. `MAXDOP 1` alone did not help; restarting SQL Server (target 653 MB) did. A real server would set `max server memory` and size the container, or the build would run off-peak.

**(d) Query Store** (on by default for new databases in SQL Server 2022) recorded the whole day for the stats statement:

| plan_id | first → last (UTC) | Shape | Executions | Avg ms | Avg reads |
|---|---|---|---|---|---|
| 51 | 15:02 → 20:00 | parallel clustered scan | 6,085 | 86.80 | 5,751 |
| 68 | 19:57 | serial scan (`MemoryGrantWarning`) | 7 | 34.10 | 4,492 |
| 72 | 20:01 → 20:02 | index seek | 46 | 2.45 | 1,103 |
| 78 | 20:15 → 20:20 | index seek + batch mode | 50,314 | 3.51 | 8 |

Plan 51's 86.8 ms average is mostly the k6 baseline: under 10 concurrent users the parallel full scan was ~6× slower than its single-user 15 ms.

**What did not help / was rejected:** the full columnstore index (same gain as the empty one, +29 % insert cost); `OPTION (RECOMPILE)` (no sniffing problem left to solve); `MAXDOP 1` as a fix for the stuck build (the request was still above the semaphore target).

### API latency (k6)

`perf/lookup.js`, 10 VUs for 60 s, 70 % of requests for the 7 most-listed books and 30 % for long-tail books (`perf/lookup-books.json`), random asking prices. Run with `PRICERADAR_LOOKUP_PER_MINUTE=100000` so the rate limiter does not interfere. Summaries: `perf/k6-before-index.json`, `perf/k6-after.json`.

| Run | Requests | avg | median | p90 | p95 | p99 | max | Checks |
|---|---|---|---|---|---|---|---|---|
| Before index | 6,055 (101/s) | 98.9 ms | 94.0 ms | 129.4 ms | **145.9 ms** | 198.0 ms | 646.6 ms | 100 % |
| After index + batch mode | 50,276 (838/s) | 11.7 ms | 10.7 ms | 16.1 ms | **18.5 ms** | 26.1 ms | 424.0 ms | 100 % |

Baseline observations (input for DAY-6):

- **Every book query scans the whole fact table.** No index starts with `BookId`, so a 14-snapshot book costs the same 4,736 page reads as an 8,275-snapshot book. CPU is 4–5× elapsed time because SQL Server parallelises the scan.
- **Elapsed time is already under NFR-2's 50 ms** at this size (12–15 ms warm), but reads are not (4,736 vs < 1,000), and reads grow linearly with the table while elapsed time hides it behind parallelism and a warm cache.
- **Q3's real cost is the window spool, not the scan:** the per-week median builds a worktable with ~50k reads. A covering index alone will not remove it; batch-mode window aggregates (a nonclustered columnstore index enables batch mode) are the experiment to try.

Sample spread (elapsed ms, 5 runs): Q1 18/15/15/15/12 · Q2 12/10/13/12/12 · Q3 39/26/44/27/42.
