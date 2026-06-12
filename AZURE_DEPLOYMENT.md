# Azure Deployment

This repository deploys the License Validation API to Azure App Service using GitHub Actions.

## Expected Azure URL

The workflow targets this Azure App Service name:

```text
serviceguardapi
```

Expected default Azure URL:

```text
https://serviceguardapi.azurewebsites.net
```

If you want a different URL, change `AZURE_WEBAPP_NAME` and `AZURE_WEBAPP_URL` in:

```text
.github/workflows/azure-app-service.yml
```

## Required Azure Resource

Create an Azure App Service for ASP.NET Core/.NET 8.

Recommended settings:

```text
Name: serviceguardapi
Runtime stack: .NET 8
OS: Linux or Windows
HTTPS Only: On
Always On: On, if available in your App Service plan
```

## Required GitHub Secret

The workflow uses an Azure App Service publish profile.

Add this GitHub repository secret:

```text
AZURE_WEBAPP_PUBLISH_PROFILE_SERVICEGUARDAPI
```

Secret value:

```text
Azure App Service publish profile XML for serviceguardapi
```

In Azure Portal:

```text
App Service > serviceguardapi > Get publish profile
```

Copy the full XML content into the GitHub secret.

## Deployment Behavior

Pull requests to `main`:

```text
restore
build
run regression tests
publish API artifact
```

Pushes to `main`:

```text
restore
build
run regression tests
publish API artifact
deploy to Azure App Service
smoke test /health/live
smoke test /health/ready
```

## Production App Settings

Configure these in Azure App Service configuration:

```text
ASPNETCORE_ENVIRONMENT=Production
Signing__KeyId=licensing-key-001
Signing__PrivateKeyPem=<stable ES256 private key PEM>
```

Without `Signing__PrivateKeyPem`, the API will run with an ephemeral development key and `/health/ready` will report `Degraded`.

## License Configuration

The deployed artifact includes:

```text
LicenseValidation.Api/licenses.json
```

For the current file-based implementation, updating license records through Git and merging to `main` will redeploy the updated `licenses.json`.

For operational license changes without redeploying from GitHub, the next recommended enhancement is one of:

```text
Azure App Configuration
Azure Blob Storage backed license file
Azure SQL / Cosmos DB license store
```

The API already uses `ILicenseStore`, so a database-backed implementation can replace the file/config implementation without changing client applications.

## Post-Deployment Checks

Open:

```text
https://serviceguardapi.azurewebsites.net/health/live
https://serviceguardapi.azurewebsites.net/health/ready
```

Expected:

```text
/health/live  = Healthy
/health/ready = Healthy when stable signing key is configured
```

Sample license validation URL:

```text
https://serviceguardapi.azurewebsites.net/api/v1/license/validate
```
