# Debugging log

Real incidents from building PriceRadar, written up the way a production post-mortem would be: symptom, how it was
investigated, root cause, fix, and what now guards against it. They are the evidence for the job ad's "production
debugging" requirement; nothing here was staged.

---

## 1. The Api crash-loops after a SQL Server restart (error 1801)

**Symptom.** `docker compose up` after a PC restart: the Api container exits, restarts, exits again; compose reports
`dependency api failed to start`, so the Worker never starts and ingestion runs stay `Queued`.

**Investigation.** `docker logs priceradar-api`:
`Applying 3 migration(s) … CREATE DATABASE [PriceRadar] … SqlException: Database 'PriceRadar' already exists`.
EF believed the database did not exist, although it did and held 500k rows. SQL Server's own log showed
`Starting up database 'PriceRadar'` *after* the container reported healthy.

**Root cause.** The SQL healthcheck runs `SELECT 1` against `master`. After a restart the server accepts logins while
the user database is still recovering; EF's existence check cannot open it, so it decides the database is missing and
runs `CREATE DATABASE`. A first fix (wait until `sys.databases.state_desc = 'ONLINE'`) was not enough: one more restart
hit the same error, because the database reports ONLINE a moment before connections to it succeed (error 4060).

**Fix.** Before migrating, the Api waits until the database is absent (fresh volume) or ONLINE **and** a real
connection to it succeeds (`DependencyInjection.WaitUntilDatabaseOnlineOrAbsentAsync`).

**Guard.** The wait is part of every startup; the symptom and both causes are in spec DAY-3/DAY-7 failure lists.

---

## 2. A columnstore index build hangs for 10 minutes (`RESOURCE_SEMAPHORE`)

**Symptom.** The DAY-6 columnstore experiment never finished; the benchmark script timed out.

**Investigation.**

```sql
SELECT session_id, command, wait_type, wait_time/1000 AS wait_s FROM sys.dm_exec_requests WHERE session_id > 50;
-- 67 | CREATE INDEX | RESOURCE_SEMAPHORE | 615
SELECT requested_memory_kb/1024 AS requested_mb, required_memory_kb/1024 AS required_mb FROM sys.dm_exec_query_memory_grants;
-- 150 | 150
SELECT pool_id, target_memory_kb/1024 AS target_mb FROM sys.dm_exec_query_resource_semaphores;
-- 128 (default pool)
SELECT TOP 3 type, pages_kb/1024 AS mb FROM sys.dm_os_memory_clerks ORDER BY pages_kb DESC;
-- MEMORYCLERK_SQLLOGPOOL 420 | MEMORYCLERK_SQLBUFFERPOOL 181
```

**Root cause.** The build needed a 150 MB memory grant (also its minimum), but the grant semaphore's target was
128 MB: the 2 GB container's memory was held by a 420 MB log-pool cache left by the bulk loads. A request larger than
the target waits forever. `MAXDOP 1` did not help (the minimum stayed 150 MB).

**Fix.** Killed the session, restarted SQL Server (target 653 MB afterwards), re-ran. The final design uses an *empty*
filtered columnstore index that builds in 29 ms.

**Guard.** Documented in spec DAY-6 and `BENCHMARKS.md`; a real server would set `max server memory` and size the
container.

---

## 3. `GET /api/books/6442` returns 500 — found through the traceId

**Symptom.** A curl check returned a clean ProblemDetails 500 with
`"traceId":"00-2a03eac0b0fd694a2c0d6de36fe4154a-…"` and no details (by design).

**Investigation.** The traceId pointed to the request, the log showed
`InvalidOperationException: The LINQ expression 'DbSet<Listing>().GroupBy(l => l.Source.Code)…' could not be translated`.
The same day a lookup returned 500 for `ORDER BY` on a record created through its constructor.

**Root cause.** EF Core cannot translate `GROUP BY` over a navigation property or ordering by a member of a
constructor-projected record. Both only fail at runtime.

**Fix.** Group on the foreign-key column and map codes afterwards; project to an anonymous type, order, then map.
Integration tests now call both endpoints.

**Guard.** Also exposed an observability gap: the Development log template did not print the trace id, so the
traceId could not be grepped. DAY-8 added `trace={TraceId}` to the template and an `X-Correlation-Id` header that is
echoed in every response and pushed into every log line (`CorrelationIdMiddleware`, test
`Responses_EchoCorrelationId_AndProblemTraceIdMatchesIt`).

---

## 4. Run IDs jump from 184 to 10181

**Symptom.** After seeding with `--reset`, the next ingestion run got id 10181.

**Root cause.** SQL Server pre-allocates identity values in a cache (10,000 for `bigint`); the unclean container stop
the night before lost the cache, so the identity advanced by 10,000. `DELETE` keeps identity values and
`DBCC CHECKIDENT … RESEED` without a value never lowers them.

**Follow-up (DAY-10).** `docker compose stop` ends `priceradar-sql` with exit code 137 even with
`stop_grace_period: 60s`: in this setup the server does not act on SIGTERM and is killed after the grace period, so
the longer period was reverted. For a clean shutdown run `SHUTDOWN` through `sqlcmd` before stopping.

**Fix.** `seed --reset` now runs `DBCC CHECKIDENT (…, RESEED, 0)` for every pipeline table, so seeded ids are
reproducible. Gaps in identity values are otherwise harmless and are not "fixed" anywhere else.

---

## 5. A failing Hangfire test that was actually correct behaviour

**Symptom.** A test moved a job to `FailedState` and expected the run to be marked Failed; it stayed Running.

**Root cause.** Hangfire's `[AutomaticRetry]` filter intercepts a `FailedState` and turns it into a scheduled retry
until the `RetryCount` job parameter reaches `Attempts`. The filter under test was right; the test did not model
exhausted retries.

**Fix.** The test sets `RetryCount` first and now covers both paths: a retried failure leaves the run Running, the
final failure marks it Failed and frees the source.

---

## 6. Benchmarks that would have lied

Two measurements were caught before they reached the README:

- **Parameter sniffing.** The first experiment showed every book compiling its own plan. Capturing the plan with
  `SET STATISTICS XML` had recompiled it. Re-done through `sys.dm_exec_query_stats` (`execution_count = 2`, cached
  `EstimateRows`) — see `db/benchmarks/02-parameter-sniffing.sql`; the misleading result file was deleted.
- **Data skew.** A plain Zipf distribution put 15 % of all snapshots on one book, which would have made the "hot book"
  benchmark unrepresentative. Switched to Zipf–Mandelbrot with an offset; the hottest book now has 8,275 of 500,000.
