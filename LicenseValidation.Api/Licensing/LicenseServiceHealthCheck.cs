using LicenseValidation.Api.Signing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LicenseValidation.Api.Licensing;

public sealed class LicenseServiceHealthCheck(
    IOptionsMonitor<LicenseOptions> licenseOptions,
    ILicenseTokenSigner tokenSigner) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = new Dictionary<string, object>
        {
            ["serverTimeUtc"] = DateTimeOffset.UtcNow,
            ["configuredLicenseCount"] = licenseOptions.CurrentValue.Licenses.Count,
            ["signingKeyId"] = tokenSigner.KeyId,
            ["signingKeyIsEphemeral"] = tokenSigner.IsEphemeral
        };

        if (licenseOptions.CurrentValue.Licenses.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "License service is running, but no licenses are configured.",
                data: data));
        }

        if (tokenSigner.IsEphemeral)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "License service is using an ephemeral development signing key.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("License service is ready.", data));
    }
}
