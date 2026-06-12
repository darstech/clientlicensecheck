# Operator Manual

This manual is for developers/operators who need to build, run, test, and rebuild the Client License Validation API on a new machine or through Docker Desktop.

For endpoint schemas, sample request/response objects, status codes, and compatibility rules, see `API_CONTRACT.md`.

## Prerequisites

Install these first:

- Git
- .NET 8 SDK or newer
- Docker Desktop, if running with containers

Check installations:

```powershell
git --version
dotnet --info
docker --version
docker compose version
```

## Fresh Dev Machine Setup

Clone the repository:

```powershell
git clone <repo-url>
cd clientlicensecheck
```

Restore dependencies:

```powershell
dotnet restore ClientLicenseCheck.slnx
```

Build the solution:

```powershell
dotnet build ClientLicenseCheck.slnx --no-restore
```

Run regression tests:

```powershell
dotnet run --project LicenseValidation.Tests/LicenseValidation.Tests.csproj --no-build
```

Run the API locally:

```powershell
dotnet run --project LicenseValidation.Api/LicenseValidation.Api.csproj
```

Open health endpoints:

```text
http://localhost:5212/health/live
http://localhost:5212/health/ready
```

Expected local behavior:

- `/health/live` should return `Healthy`.
- `/health/ready` may return `Degraded` if no stable signing key is configured. This is expected for local development.

## Local API Test Request

Use PowerShell:

```powershell
$body = @{
  schemaVersion = "1.0"
  clientId = "pharmacloud-fakhirgroup-com"
  applicationId = "pharmacloud"
  environment = "production"
  activationId = "pharmacloud-fakhirgroup-com-pharmacloud-production"
  installationId = "2f4b5f84-91d6-4b28-b0a8-59bdf57c927a"
  appVersion = "1.0.0"
  requestId = [guid]::NewGuid().ToString()
  extra = @{
    tenantCount = 25
    region = "UAE"
  }
} | ConvertTo-Json -Depth 8

Invoke-RestMethod `
  -Uri http://localhost:5212/api/v1/license/validate `
  -Method Post `
  -ContentType "application/json" `
  -Body $body
```

Expected result:

```text
valid: true
status: active
code: LICENSE_ACTIVE
message: License active.
```

## Docker Build And Run

Build the Docker image:

```powershell
docker compose build
```

Start the API:

```powershell
docker compose up -d
```

Check container status:

```powershell
docker compose ps
```

Check logs:

```powershell
docker compose logs -f license-validation-api
```

Test health:

```powershell
curl http://localhost:5212/health/live
curl http://localhost:5212/health/ready
```

Stop the API:

```powershell
docker compose down
```

Stop and remove local container, network, and anonymous volumes:

```powershell
docker compose down --volumes
```

## Docker Without Compose

Build directly:

```powershell
docker build -t clientlicensecheck/license-validation-api:local .
```

Run directly:

```powershell
docker run -d `
  --name license-validation-api `
  -p 5212:8080 `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e ASPNETCORE_URLS=http://+:8080 `
  -e DOTNET_USE_POLLING_FILE_WATCHER=true `
  -e Signing__KeyId=licensing-key-001 `
  -e Signing__PrivateKeyPem="" `
  -v ${PWD}/LicenseValidation.Api/licenses.json:/app/licenses.json:ro `
  clientlicensecheck/license-validation-api:local
```

Check logs:

```powershell
docker logs -f license-validation-api
```

Stop and remove:

```powershell
docker stop license-validation-api
docker rm license-validation-api
```

## Daily Development Workflow

After pulling latest changes:

```powershell
git pull
dotnet restore ClientLicenseCheck.slnx
dotnet build ClientLicenseCheck.slnx --no-restore
dotnet run --project LicenseValidation.Tests/LicenseValidation.Tests.csproj --no-build
```

Run API for local development:

```powershell
dotnet run --project LicenseValidation.Api/LicenseValidation.Api.csproj
```

## Building A New Feature

Recommended sequence:

1. Create or switch to a feature branch.
2. Make code/config/test changes.
3. Build locally.
4. Run regression tests.
5. Run the API locally.
6. Test health and license validation endpoint.
7. Rebuild Docker image.
8. Run Docker container and test again.

Commands:

```powershell
git checkout -b codex/my-feature-name

dotnet restore ClientLicenseCheck.slnx
dotnet build ClientLicenseCheck.slnx --no-restore
dotnet run --project LicenseValidation.Tests/LicenseValidation.Tests.csproj --no-build

dotnet run --project LicenseValidation.Api/LicenseValidation.Api.csproj
```

In a second terminal, test:

```powershell
curl http://localhost:5212/health/live
curl http://localhost:5212/health/ready
```

Then rebuild Docker:

```powershell
docker compose down
docker compose build
docker compose up -d
docker compose ps
```

Test Docker-hosted API:

```powershell
curl http://localhost:5212/health/live
curl http://localhost:5212/health/ready
```

## Rebuild After Code Changes

For normal .NET local run:

```powershell
dotnet build ClientLicenseCheck.slnx --no-restore
dotnet run --project LicenseValidation.Tests/LicenseValidation.Tests.csproj --no-build
dotnet run --project LicenseValidation.Api/LicenseValidation.Api.csproj
```

For Docker Compose:

```powershell
docker compose down
docker compose up --build -d
docker compose logs -f license-validation-api
```

For a clean Docker rebuild with no build cache:

```powershell
docker compose down
docker compose build --no-cache
docker compose up -d
```

## Rebuild After Configuration Changes

If only `licenses.json`, `appsettings.json`, or `docker-compose.yml` changed:

```powershell
docker compose down
docker compose up --build -d
```

If only environment variables changed in `docker-compose.yml`, a rebuild may not be required, but recreating the container is required:

```powershell
docker compose up -d --force-recreate
```

## Signing Key Operations

Local development can run without `Signing__PrivateKeyPem`. The API generates an ephemeral development key.

Production must use a stable ES256 private key:

```text
Signing__KeyId=licensing-key-001
Signing__PrivateKeyPem=<ES256 private key PEM>
```

When no stable key is configured:

```text
/health/live  = Healthy
/health/ready = Degraded
```

When a stable key is configured and licenses exist:

```text
/health/live  = Healthy
/health/ready = Healthy
```

Do not give the private key to client applications. Clients should only receive the matching public key if they need to verify signed tokens.

## License Configuration

Initial licenses are stored in:

```text
LicenseValidation.Api/licenses.json
```

Main fields:

```text
ClientId
ApplicationId
Status
ExpiresAtUtc
AllowedEnvironments
AllowedActivationIds
AllowedInstallationIds
Features
Limits
Messages
```

When running locally with `dotnet run`, the API watches `licenses.json` and reloads license changes automatically. A rebuild is not required for license-data changes.

When running with Docker Compose, `licenses.json` is bind-mounted into the container:

```text
./LicenseValidation.Api/licenses.json:/app/licenses.json:ro
```

This means you can edit `LicenseValidation.Api/licenses.json` on the host machine and the running container can pick up the change without rebuilding the Docker image.

After changing only license configuration, test the running API:

```powershell
curl http://localhost:5212/health/ready
```

If Docker Desktop file watching is delayed, recreate the container without rebuilding:

```powershell
docker compose up -d --force-recreate
```

After changing license code or application behavior:

```powershell
dotnet build ClientLicenseCheck.slnx --no-restore
dotnet run --project LicenseValidation.Tests/LicenseValidation.Tests.csproj --no-build
docker compose up --build -d
```

## Common Troubleshooting

Port already in use:

```powershell
docker compose down
```

Then retry:

```powershell
docker compose up -d
```

Container is running but API is not responding:

```powershell
docker compose ps
docker compose logs license-validation-api
```

Docker image seems stale:

```powershell
docker compose down
docker compose build --no-cache
docker compose up -d
```

.NET build assets missing:

```powershell
dotnet restore ClientLicenseCheck.slnx
dotnet build ClientLicenseCheck.slnx --no-restore
```

Readiness is degraded:

```text
This is expected if the signing key is ephemeral.
Configure Signing__PrivateKeyPem for production readiness.
```

## Pre-Deployment Checklist

Before deploying a new version:

- `dotnet restore ClientLicenseCheck.slnx`
- `dotnet build ClientLicenseCheck.slnx --no-restore`
- `dotnet run --project LicenseValidation.Tests/LicenseValidation.Tests.csproj --no-build`
- `docker compose build`
- `docker compose up -d`
- Verify `http://localhost:5212/health/live`
- Verify `http://localhost:5212/health/ready`
- Verify sample license validation returns expected result
- Confirm production signing key is configured outside source control
