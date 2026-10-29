# Build context: repository root.  docker build -f src/PriceRadar.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/PriceRadar.Core/PriceRadar.Core.csproj src/PriceRadar.Core/
COPY src/PriceRadar.Infrastructure/PriceRadar.Infrastructure.csproj src/PriceRadar.Infrastructure/
COPY src/PriceRadar.Worker/PriceRadar.Worker.csproj src/PriceRadar.Worker/
RUN dotnet restore src/PriceRadar.Worker/PriceRadar.Worker.csproj

COPY src/ src/
RUN dotnet publish src/PriceRadar.Worker/PriceRadar.Worker.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "PriceRadar.Worker.dll"]
