using Microsoft.Extensions.Options;

namespace LicenseValidation.Api.Licensing;

public sealed class ConfigurationLicenseStore(IOptionsMonitor<LicenseOptions> options) : ILicenseStore
{
    public Task<LicenseEvaluation> ValidateAsync(
        LicenseValidationRequest request,
        DateTimeOffset serverTimeUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validationError = ValidateShape(request);
        if (validationError is not null)
        {
            return Task.FromResult(Invalid("LICENSE_REQUEST_INVALID", validationError));
        }

        var license = options.CurrentValue.Licenses.FirstOrDefault(candidate =>
            EqualsIgnoreCase(candidate.ClientId, request.ClientId) &&
            EqualsIgnoreCase(candidate.ApplicationId, request.ApplicationId));

        if (license is null)
        {
            return Task.FromResult(Invalid("LICENSE_NOT_FOUND", options.CurrentValue.DefaultInvalidMessage));
        }

        if (!ContainsIgnoreCase(license.AllowedEnvironments, request.Environment))
        {
            return Task.FromResult(Invalid("LICENSE_ENVIRONMENT_NOT_ALLOWED", license.Messages.Invalid));
        }

        if (!ContainsIgnoreCase(license.AllowedActivationIds, request.ActivationId))
        {
            return Task.FromResult(Invalid("LICENSE_ACTIVATION_NOT_ALLOWED", license.Messages.Invalid));
        }

        if (!ContainsIgnoreCase(license.AllowedInstallationIds, request.InstallationId))
        {
            return Task.FromResult(Invalid("LICENSE_INSTALLATION_NOT_ALLOWED", license.Messages.Invalid));
        }

        if (EqualsIgnoreCase(license.Status, LicenseStatuses.Suspended))
        {
            return Task.FromResult(new LicenseEvaluation(
                false,
                LicenseStatuses.Suspended,
                "LICENSE_SUSPENDED",
                license.Messages.Suspended,
                license.Features,
                license.Limits));
        }

        if (license.ExpiresAtUtc is not null && license.ExpiresAtUtc <= serverTimeUtc)
        {
            return Task.FromResult(new LicenseEvaluation(
                false,
                LicenseStatuses.Expired,
                "LICENSE_EXPIRED",
                license.Messages.Expired,
                license.Features,
                license.Limits));
        }

        if (!EqualsIgnoreCase(license.Status, LicenseStatuses.Active))
        {
            return Task.FromResult(Invalid("LICENSE_INVALID_STATUS", license.Messages.Invalid));
        }

        return Task.FromResult(new LicenseEvaluation(
            true,
            LicenseStatuses.Active,
            "LICENSE_ACTIVE",
            license.Messages.Active,
            license.Features,
            license.Limits));
    }

    private static LicenseEvaluation Invalid(string code, string message) =>
        new(false, LicenseStatuses.Invalid, code, message, [], EmptyLimits());

    private static Dictionary<string, object?> EmptyLimits() => [];

    private static string? ValidateShape(LicenseValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return "Activation failed. Missing client id.";
        }

        if (string.IsNullOrWhiteSpace(request.ApplicationId))
        {
            return "Activation failed. Missing application id.";
        }

        if (string.IsNullOrWhiteSpace(request.Environment))
        {
            return "Activation failed. Missing environment.";
        }

        if (string.IsNullOrWhiteSpace(request.ActivationId))
        {
            return "Activation failed. Missing activation id.";
        }

        if (string.IsNullOrWhiteSpace(request.InstallationId))
        {
            return "Activation failed. Missing installation id.";
        }

        return null;
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string expected) =>
        values.Any(value => EqualsIgnoreCase(value, expected));

    private static bool EqualsIgnoreCase(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
