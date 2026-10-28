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

## Query benchmarks (DAY-4 baseline, DAY-6 optimisation)

*To be filled on DAY-4 / DAY-6 using the template in spec §11.4.*
