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

## Required GitHub Secrets

The workflow uses GitHub OIDC with `azure/login`.

Add these GitHub repository secrets:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
```

The Azure app registration or managed identity behind `AZURE_CLIENT_ID` needs permission to deploy to the App Service. A practical starting role is:

```text
Website Contributor
```

Scoped to:

```text
App Service > serviceguardapi
```

You must also configure a federated credential in Azure for this GitHub repository and branch/environment so GitHub Actions can request an OIDC token without storing an Azure password.

Recommended federated credential subject for deployments from `main`:

```text
repo:darstech/clientlicensecheck:ref:refs/heads/main
```

If you use GitHub Environments, configure the subject to match your environment policy.

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
