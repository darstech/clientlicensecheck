namespace LicenseValidation.Api.Signing;

public sealed class SigningOptions
{
    public const string SectionName = "Signing";

    public string KeyId { get; init; } = "licensing-key-001";

    public string? PrivateKeyPem { get; init; }
}
