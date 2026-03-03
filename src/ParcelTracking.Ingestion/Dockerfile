# ── Stage 1: build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo

# Copy solution + every project file first (layer-cache friendly)
COPY ParcelTracking.sln ./
COPY src/ParcelTracking.Domain/ParcelTracking.Domain.csproj             src/ParcelTracking.Domain/
COPY src/ParcelTracking.Rules/ParcelTracking.Rules.csproj               src/ParcelTracking.Rules/
COPY src/ParcelTracking.Notifications/ParcelTracking.Notifications.csproj src/ParcelTracking.Notifications/
COPY src/ParcelTracking.Infrastructure/ParcelTracking.Infrastructure.csproj src/ParcelTracking.Infrastructure/
COPY src/ParcelTracking.Ingestion/ParcelTracking.Ingestion.csproj       src/ParcelTracking.Ingestion/

RUN dotnet restore src/ParcelTracking.Ingestion/ParcelTracking.Ingestion.csproj

# Copy all source code and publish
COPY src/ src/
RUN dotnet publish src/ParcelTracking.Ingestion/ParcelTracking.Ingestion.csproj \
    -c Release -o /app/publish --no-restore

# ── Stage 2: runtime ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "ParcelTracking.Ingestion.dll"]
