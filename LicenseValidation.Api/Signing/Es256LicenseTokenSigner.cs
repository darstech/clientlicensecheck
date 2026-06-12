using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LicenseValidation.Api.Signing;

public sealed class Es256LicenseTokenSigner : ILicenseTokenSigner
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ECDsa signingKey;
    private readonly object signingLock = new();

    public Es256LicenseTokenSigner(IOptions<SigningOptions> options, ILogger<Es256LicenseTokenSigner> logger)
    {
        KeyId = options.Value.KeyId;
        signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        if (string.IsNullOrWhiteSpace(options.Value.PrivateKeyPem))
        {
            IsEphemeral = true;
            logger.LogWarning(
                "No Signing:PrivateKeyPem configured. Generated an ephemeral development signing key. Configure a stable ES256 private key before using this service with real clients.");
            return;
        }

        signingKey.ImportFromPem(options.Value.PrivateKeyPem);
    }

    public string KeyId { get; }

    public bool IsEphemeral { get; }

    public string Sign(LicenseTokenPayload payload)
    {
        var header = new
        {
            alg = "ES256",
            typ = "JWT",
            kid = KeyId
        };

        var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header, SerializerOptions));
        var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions));
        var data = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedPayload}");

        byte[] signature;
        lock (signingLock)
        {
            signature = signingKey.SignData(
                data,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        return $"{encodedHeader}.{encodedPayload}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
