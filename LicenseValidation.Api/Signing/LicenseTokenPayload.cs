namespace LicenseValidation.Api.Signing;

public sealed record LicenseTokenPayload(
    string SchemaVersion,
    bool Valid,
    string Status,
    string Code,
    string Message,
    string ClientId,
    string ApplicationId,
    string Environment,
    string ActivationId,
    string InstallationId,
    DateTimeOffset ServerTimeUtc,
    DateTimeOffset TokenValidUntilUtc,
    IReadOnlyList<string> Features,
    IReadOnlyDictionary<string, object?> Limits);
