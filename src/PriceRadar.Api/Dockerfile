# Build context: repository root.  docker build -f src/PriceRadar.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/PriceRadar.Core/PriceRadar.Core.csproj src/PriceRadar.Core/
COPY src/PriceRadar.Infrastructure/PriceRadar.Infrastructure.csproj src/PriceRadar.Infrastructure/
COPY src/PriceRadar.Api/PriceRadar.Api.csproj src/PriceRadar.Api/
RUN dotnet restore src/PriceRadar.Api/PriceRadar.Api.csproj

COPY src/ src/
RUN dotnet publish src/PriceRadar.Api/PriceRadar.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
# curl is only for the container healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=6 CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "PriceRadar.Api.dll"]
