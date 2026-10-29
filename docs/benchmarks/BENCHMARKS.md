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
| 5 | Q1–Q3 | + `IX_PriceSnapshot_Book_Date` (DAY-6) | | | | | | |

Baseline observations (input for DAY-6):

- **Every book query scans the whole fact table.** No index starts with `BookId`, so a 14-snapshot book costs the same 4,736 page reads as an 8,275-snapshot book. CPU is 4–5× elapsed time because SQL Server parallelises the scan.
- **Elapsed time is already under NFR-2's 50 ms** at this size (12–15 ms warm), but reads are not (4,736 vs < 1,000), and reads grow linearly with the table while elapsed time hides it behind parallelism and a warm cache.
- **Q3's real cost is the window spool, not the scan:** the per-week median builds a worktable with ~50k reads. A covering index alone will not remove it; batch-mode window aggregates (a nonclustered columnstore index enables batch mode) are the experiment to try.

Sample spread (elapsed ms, 5 runs): Q1 18/15/15/15/12 · Q2 12/10/13/12/12 · Q3 39/26/44/27/42.
