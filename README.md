# PriceRadar

Market price intelligence for used books on Serbian marketplaces. Ask, at a flea-market stall, whether the price a seller wants is cheap, fair or expensive.

> Work in progress: built in 10 days from the specification in [`docs/spec/PriceRadar-Spec.md`](docs/spec/PriceRadar-Spec.md) ([PDF](docs/spec/PriceRadar-Spec.pdf)). The full README follows the outline in the spec's Appendix A on DAY-9.

## Quick start (DAY-3 state)

Prerequisites: Docker Desktop, .NET SDK 9.0.3xx (pinned in `global.json`).

```powershell
Copy-Item .env.example .env            # then set a strong MSSQL_SA_PASSWORD
docker compose up -d --build           # SQL Server (localhost,14333), Api (localhost:5080), Worker (Hangfire)
curl.exe -s http://localhost:5080/health/ready

# Synthetic warehouse: 20k books, 60k listings, 500k price snapshots (~15–30 s)
dotnet user-secrets set "ConnectionStrings:Sql" "Server=localhost,14333;Database=PriceRadar;User Id=sa;Password=<your password>;TrustServerCertificate=True" --project src/PriceRadar.Api
dotnet run --project tools/PriceRadar.Seeder -c Release -- seed --seed 42 --reset

# Trigger an ingestion run and watch it at http://localhost:5080/hangfire
curl.exe -s -X POST http://localhost:5080/api/ingestion/runs -H "Content-Type: application/json" -d '{\"source\":\"fixture\",\"fixtureSet\":\"run-001\"}'
curl.exe -s http://localhost:5080/api/ingestion/runs
```

Running the Api outside Docker: `dotnet tool restore`, then `dotnet run --project src/PriceRadar.Api` (it uses the same user-secrets connection string and applies migrations on startup).

## Solution layout

| Project | Role |
|---|---|
| `src/PriceRadar.Core` | Domain and application logic, no I/O |
| `src/PriceRadar.Infrastructure` | EF Core model and migrations, SQL scripts; Dapper, sources, Claude client later |
| `src/PriceRadar.Api` | ASP.NET Core Web API, health checks, OpenAPI (`/openapi/v1.json`, `/scalar` in Development) |
| `src/PriceRadar.Worker` | Worker Service that will host the Hangfire server (DAY-3) |
| `tools/PriceRadar.Seeder` | Synthetic data generator (DAY-2) |
| `tests/*` | xUnit + Shouldly unit tests; Testcontainers integration tests from DAY-4 |

## Matching quality

Listings are matched to canonical books without ISBNs: Serbian-aware normalisation (Cyrillic → Latin, diacritics, noise words such as "kao nova", years, roman volume numerals), an inverted index for candidates, and a score of `0.7 · title + 0.3 · author` (FuzzySharp token ratios, Jaro-Winkler for names). Clear winners are matched automatically; the uncertain middle band goes to the Claude API, at most once per distinct string thanks to a database cache.

Measured on a 169-listing golden set (`tests/fixtures/matching/`) with the chosen thresholds (auto 0.90, new book 0.65, margin 0.05):

| Auto-match precision | Auto-match recall | Sent to Claude | Missed duplicates | Wrong auto-matches |
|---|---|---|---|---|
| 100.0 % | 88.7 % | 11.8 % | 0 | 0 |

On the fixture data, fuzzy matching cut new-book creation for the first run from 52 to 21 books compared with exact matching. Run `dotnet test tests/PriceRadar.UnitTests --filter Category=Golden --logger "console;verbosity=detailed"` to print the full threshold grid.

AI resolution is optional. Set a key for the Worker to enable it (it is never committed):

```powershell
dotnet user-secrets set "Ai:ApiKey" "<your Anthropic API key>" --project src/PriceRadar.Worker   # dotnet run
# or ANTHROPIC_API_KEY=... in .env for docker compose
```

## Scraping policy

Live marketplace sources are disabled by default and intended for personal local use only. See spec §4.5.
