# PriceRadar

Market price intelligence for used books on Serbian marketplaces. Ask, at a flea-market stall, whether the price a seller wants is cheap, fair or expensive.

> Work in progress: built in 10 days from the specification in [`docs/spec/PriceRadar-Spec.md`](docs/spec/PriceRadar-Spec.md) ([PDF](docs/spec/PriceRadar-Spec.pdf)). The full README follows the outline in the spec's Appendix A on DAY-9.

## Quick start (DAY-1 state)

Prerequisites: Docker Desktop, .NET SDK 9.0.3xx (pinned in `global.json`).

```powershell
Copy-Item .env.example .env            # then set a strong MSSQL_SA_PASSWORD
docker compose up -d sql               # SQL Server 2022 on localhost,14333
dotnet tool restore                    # dotnet-ef 9.x (local tool manifest)
dotnet user-secrets set "ConnectionStrings:Sql" "Server=localhost,14333;Database=PriceRadar;User Id=sa;Password=<your password>;TrustServerCertificate=True" --project src/PriceRadar.Api
dotnet ef database update -p src/PriceRadar.Infrastructure -s src/PriceRadar.Api
dotnet run --project src/PriceRadar.Api
curl.exe -s http://localhost:5080/health/ready
```

## Solution layout

| Project | Role |
|---|---|
| `src/PriceRadar.Core` | Domain and application logic, no I/O |
| `src/PriceRadar.Infrastructure` | EF Core model and migrations, SQL scripts; Dapper, sources, Claude client later |
| `src/PriceRadar.Api` | ASP.NET Core Web API, health checks, OpenAPI (`/openapi/v1.json`, `/scalar` in Development) |
| `src/PriceRadar.Worker` | Worker Service that will host the Hangfire server (DAY-3) |
| `tools/PriceRadar.Seeder` | Synthetic data generator (DAY-2) |
| `tests/*` | xUnit + Shouldly unit tests; Testcontainers integration tests from DAY-4 |

## Scraping policy

Live marketplace sources are disabled by default and intended for personal local use only. See spec §4.5.
