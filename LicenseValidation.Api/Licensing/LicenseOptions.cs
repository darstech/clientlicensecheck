namespace LicenseValidation.Api.Licensing;

public sealed class LicenseOptions
{
    public const string SectionName = "Licensing";

    public string DefaultInvalidMessage { get; init; } = "Activation failed. Please contact support.";

    public List<ConfiguredLicense> Licenses { get; init; } = [];
}

public sealed class ConfiguredLicense
{
    public string ClientId { get; init; } = "";

    public string ApplicationId { get; init; } = "";

    public string Status { get; init; } = LicenseStatuses.Active;

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public List<string> AllowedEnvironments { get; init; } = [];

    public List<string> AllowedActivationIds { get; init; } = [];

    public List<string> AllowedInstallationIds { get; init; } = [];

    public List<string> Features { get; init; } = [];

    public Dictionary<string, object?> Limits { get; init; } = [];

    public LicenseMessages Messages { get; init; } = new();
}

public sealed class LicenseMessages
{
    public string Active { get; init; } = "License active.";

    public string Expired { get; init; } = "Activation failed. License has expired.";

    public string Suspended { get; init; } = "Activation failed. Please contact support.";

    public string Invalid { get; init; } = "Activation failed. Please contact support.";
}

public static class LicenseStatuses
{
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Suspended = "suspended";
    public const string Invalid = "invalid";
}
