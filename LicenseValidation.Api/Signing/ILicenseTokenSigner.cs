namespace LicenseValidation.Api.Signing;

public interface ILicenseTokenSigner
{
    string KeyId { get; }

    bool IsEphemeral { get; }

    string Sign(LicenseTokenPayload payload);
}
