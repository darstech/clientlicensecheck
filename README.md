# Client License Check

Lightweight centralized license validation API for client-deployed applications.

For build, run, Docker, rebuild, and operational commands, see `OPERATOR_MANUAL.md`.

For endpoint schemas, sample request/response objects, status codes, and compatibility rules, see `API_CONTRACT.md`.

The first implementation is intentionally simple:

- ASP.NET Core Minimal API on .NET 8.
- Azure App Service friendly.
- Free Azure HTTPS endpoint supported out of the box.
- Config-backed licenses for the initial stage.
- One-day signed validation tokens.
- Health endpoints with millisecond response timing.
- Stable JSON contract for clients built in .NET, Java, PHP, Node, Python, or any other stack.

## Architecture

```text
Client Application
  |
  | POST /api/v1/license/validate
  v
License Validation API
  |
  | ConfigurationLicenseStore today
  | Database-backed ILicenseStore later
  v
License Evaluation + ES256 Signed Token
```

The API uses Azure/server UTC time. Client machine time is not trusted for license expiry.

## Endpoints

### Liveness

```http
GET /health/live
```

Fast process-level health check.

### Readiness

```http
GET /health/ready
```

Checks whether the license service is ready and reports configured license count, signing key id, and response timing.

### License Validation

```http
POST /api/v1/license/validate
```

Sample request:

```json
{
  "schemaVersion": "1.0",
  "clientId": "pharmacloud-fakhirgroup-com",
  "applicationId": "pharmacloud",
  "environment": "production",
  "activationId": "pharmacloud-fakhirgroup-com-pharmacloud-production",
  "installationId": "2f4b5f84-91d6-4b28-b0a8-59bdf57c927a",
  "appVersion": "1.0.0",
  "requestId": "unique-request-id",
  "extra": {
    "tenantCount": 25,
    "region": "UAE"
  }
}
```

Sample response:

```json
{
  "valid": true,
  "status": "active",
  "code": "LICENSE_ACTIVE",
  "message": "License active.",
  "serverTimeUtc": "2026-06-12T16:00:00Z",
  "tokenValidUntilUtc": "2026-06-13T16:00:00Z",
  "features": ["core", "reports"],
  "limits": {
    "maxTenants": null,
    "maxUsers": null
  },
  "responseTimeMs": 0.8,
  "token": "eyJhbGciOiJFUzI1NiIs..."
}
```

## License Identity Model

Use these fields from day one:

```text
clientId        Customer/reseller/company.
applicationId   Product identifier, for example pharmacloud.
environment     production, test, staging, demo.
activationId    Client + product + environment activation record.
installationId  Fixed GUID for the deployed instance.
extra           Future expansion object. Older clients can omit future fields.
```

For the same client:

```text
clientId: pharma-reseller-abc
applicationId: pharmacloud
environment: production
activationId: pharma-reseller-abc-pharmacloud-production

clientId: pharma-reseller-abc
applicationId: pharmacloud
environment: test
activationId: pharma-reseller-abc-pharmacloud-test
```

## Client Behavior

Recommended client behavior:

1. On first login of the day, call the validation API.
2. If `valid=true`, cache the response token until `tokenValidUntilUtc`.
3. For additional logins during the same token window, use the cached result.
4. If the API is unreachable, block login and show a local fallback activation-failed message.
5. If the API returns `valid=false`, display the API-provided `message`.
6. If local machine time appears earlier than the last known `serverTimeUtc`, ignore local cache and call the API again.

No grace period is implemented by design.

## Signing

Responses are signed as compact ES256 JWT-style tokens.

For local development, the API generates an ephemeral signing key if `Signing:PrivateKeyPem` is empty. For Azure or any real client, configure a stable private key through App Service configuration or Key Vault:

```text
Signing__KeyId=licensing-key-001
Signing__PrivateKeyPem=<ES256 private key PEM>
```

Clients only need the matching public key to verify the token. They must never receive the private key.

## Configure Licenses

Initial licenses are configured in `LicenseValidation.Api/licenses.json` under `Licensing:Licenses`.

The API loads `licenses.json` with change reload enabled, so license-data edits do not require rebuilding the application. Docker Compose bind-mounts this file into the container for local testing.

Later, replace `ConfigurationLicenseStore` with a database implementation of `ILicenseStore` without changing the API contract.

## Run Locally

```powershell
dotnet restore ClientLicenseCheck.slnx
dotnet run --project LicenseValidation.Api/LicenseValidation.Api.csproj
```

Then open:

```text
http://localhost:5212/health/live
http://localhost:5212/health/ready
```

Use `LicenseValidation.Api/LicenseValidation.Api.http` for sample requests.

## Run With Docker Desktop

Build and start the API:

```powershell
docker compose up --build
```

The container listens internally on port `8080` and is mapped locally to:

```text
http://localhost:5212
```

Check the service:

```powershell
curl http://localhost:5212/health/live
curl http://localhost:5212/health/ready
```

Validate the sample Pharmacloud license:

```powershell
curl -X POST http://localhost:5212/api/v1/license/validate `
  -H "Content-Type: application/json" `
  -d "{ \"schemaVersion\": \"1.0\", \"clientId\": \"pharmacloud-fakhirgroup-com\", \"applicationId\": \"pharmacloud\", \"environment\": \"production\", \"activationId\": \"pharmacloud-fakhirgroup-com-pharmacloud-production\", \"installationId\": \"2f4b5f84-91d6-4b28-b0a8-59bdf57c927a\", \"appVersion\": \"1.0.0\", \"requestId\": \"manual-test\", \"extra\": { \"tenantCount\": 25, \"region\": \"UAE\" } }"
```

Stop the local container:

```powershell
docker compose down
```

Local Docker runs with an ephemeral development signing key unless you provide `Signing__PrivateKeyPem` in `docker-compose.yml` or through your deployment environment. Because of that, `/health/ready` reports `Degraded` locally by design. `/health/live` should remain `Healthy`.

## Run Tests

```powershell
dotnet restore ClientLicenseCheck.slnx
dotnet build ClientLicenseCheck.slnx --no-restore
dotnet run --project LicenseValidation.Tests/LicenseValidation.Tests.csproj --no-build
```

The test runner protects the stable client contract and core licensing behavior:

- active, expired, suspended, and unknown-client responses
- production/test environment control for the same client
- future `extra` request fields
- server-time-based expiry
- required activation fields
- ES256 token shape and signature verification
- readiness health behavior for development and production signing keys
