# ---------------------------------------------------------------------------
# API Dockerfile — multi-stage build for InvoiceTracker.Api.
#
# Build context is expected to be the `backend/` directory (see
# docker-compose.yml's `build.context` for this service), matching the
# sibling frontend/Dockerfile convention — paths below are relative to
# `backend/`, not the repo root.
# ---------------------------------------------------------------------------

# ---- Stage 1: build -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the .csproj files first (in dependency order) so `dotnet restore`
# is cached across source-only changes — this layer only invalidates when a
# .csproj itself changes, not on every .cs edit.
COPY src/InvoiceTracker.Domain/InvoiceTracker.Domain.csproj src/InvoiceTracker.Domain/
COPY src/InvoiceTracker.Application/InvoiceTracker.Application.csproj src/InvoiceTracker.Application/
COPY src/InvoiceTracker.Infrastructure/InvoiceTracker.Infrastructure.csproj src/InvoiceTracker.Infrastructure/
COPY src/InvoiceTracker.Api/InvoiceTracker.Api.csproj src/InvoiceTracker.Api/

# Restoring just the API project's csproj transitively restores its
# ProjectReferences (Domain/Application/Infrastructure) too — the test
# projects aren't part of the published image, so they're deliberately never
# copied into this stage at all.
RUN dotnet restore src/InvoiceTracker.Api/InvoiceTracker.Api.csproj

# Now bring in the rest of the source and publish.
COPY src/ src/
RUN dotnet publish src/InvoiceTracker.Api/InvoiceTracker.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ---- Stage 2: runtime -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build --chown=app:app /app/publish .

# launchSettings.json's "http" profile (local, non-Docker `dotnet run`) binds
# Kestrel to port 5100, but launchSettings only applies to `dotnet run`/IIS
# Express — it has no effect on the published binary this image runs, so the
# listen URL has to be restated explicitly here via ASPNETCORE_URLS to match.
ENV ASPNETCORE_URLS=http://+:5100
EXPOSE 5100

# Everything else the app needs at runtime is supplied by docker-compose.yml
# as env vars at container-start time, never baked into this image, e.g.:
#   ASPNETCORE_ENVIRONMENT             (Program.cs/JwtSecretGuard: outside
#                                        Development, Jwt__Secret below is
#                                        mandatory — see next line)
#   Jwt__Secret                        (JwtSecretGuard.EnsureConfigured hard-
#                                        refuses to boot without a real value
#                                        outside Development; appsettings.json
#                                        ships no production value on purpose)
#   ConnectionStrings__DefaultConnection
#   Kafka__BootstrapServers (+ Kafka__OverdueTopic / Kafka__ConsumerGroupId /
#                             Kafka__OverdueScanIntervalMinutes /
#                             Kafka__OutboxRelayIntervalSeconds)
#   Cors__AllowedOrigins__0
#   Frontend__BaseUrl
#   Resend__ApiKey (+ Resend__FromAddress)
# appsettings.json (the base/production config) intentionally ships empty/
# placeholder values for all of these — see each setting's own "_comment" —
# so the container must receive real values at `docker run`/compose time.

# mcr.microsoft.com/dotnet/aspnet:8.0 ships a built-in non-root "app" user
# (see Microsoft's .NET container image docs) — switch to it instead of
# running as the image's default root user.
USER app

ENTRYPOINT ["dotnet", "InvoiceTracker.Api.dll"]
