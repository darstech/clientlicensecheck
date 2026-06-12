using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LicenseValidation.Api.Licensing;
using LicenseValidation.Api.Signing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var tests = new (string Name, Func<Task> Run)[]
{
    ("active production license validates", ActiveProductionLicenseValidates),
    ("same client can validate test environment", SameClientCanValidateTestEnvironment),
    ("unknown extra fields do not break request contract", UnknownExtraFieldsDoNotBreakRequestContract),
    ("unknown client returns invalid response instead of throwing", UnknownClientReturnsInvalid),
    ("disallowed environment is centrally denied", DisallowedEnvironmentIsDenied),
    ("suspended license returns central suspended message", SuspendedLicenseReturnsCentralMessage),
    ("expiry uses server time only", ExpiryUsesServerTimeOnly),
    ("missing required fields are rejected", MissingRequiredFieldsAreRejected),
    ("token signer emits verifiable ES256 token", TokenSignerEmitsVerifiableEs256Token),
    ("readiness is degraded for ephemeral development signing key", ReadinessIsDegradedForEphemeralKey),
    ("readiness is healthy for configured stable signing key", ReadinessIsHealthyForStableKey)
};

var failed = 0;

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(exception);
    }
}

if (failed > 0)
{
    Console.WriteLine($"{failed} test(s) failed.");
    return 1;
}

Console.WriteLine($"{tests.Length} test(s) passed.");
return 0;

static async Task ActiveProductionLicenseValidates()
{
    var evaluation = await CreateStore().ValidateAsync(
        Request(environment: "production", activationId: "client-a-pharmacloud-production", installationId: "prod-installation"),
        DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
        CancellationToken.None);

    AssertTrue(evaluation.Valid);
    AssertEqual(LicenseStatuses.Active, evaluation.Status);
    AssertEqual("LICENSE_ACTIVE", evaluation.Code);
    AssertEqual("License active.", evaluation.Message);
    AssertEqual("core", evaluation.Features[0]);
    AssertTrue(evaluation.Limits.ContainsKey("maxTenants"));
}

static async Task SameClientCanValidateTestEnvironment()
{
    var evaluation = await CreateStore().ValidateAsync(
        Request(environment: "test", activationId: "client-a-pharmacloud-test", installationId: "test-installation"),
        DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
        CancellationToken.None);

    AssertTrue(evaluation.Valid);
    AssertEqual("LICENSE_ACTIVE", evaluation.Code);
}

static async Task UnknownExtraFieldsDoNotBreakRequestContract()
{
    const string json = """
    {
      "schemaVersion": "1.0",
      "clientId": "client-a",
      "applicationId": "pharmacloud",
      "environment": "production",
      "activationId": "client-a-pharmacloud-production",
      "installationId": "prod-installation",
      "appVersion": "2.1.0",
      "requestId": "request-123",
      "futureTopLevelField": "ignored",
      "extra": {
        "tenantCount": 25,
        "futureNestedField": {
          "name": "value"
        }
      }
    }
    """;

    var request = JsonSerializer.Deserialize<LicenseValidationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    AssertNotNull(request);
    AssertNotNull(request!.Extra);
    AssertTrue(request.Extra!.ContainsKey("futureNestedField"));

    var evaluation = await CreateStore().ValidateAsync(
        request,
        DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
        CancellationToken.None);

    AssertTrue(evaluation.Valid);
}

static async Task UnknownClientReturnsInvalid()
{
    var evaluation = await CreateStore().ValidateAsync(
        Request(clientId: "not-configured"),
        DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
        CancellationToken.None);

    AssertFalse(evaluation.Valid);
    AssertEqual(LicenseStatuses.Invalid, evaluation.Status);
    AssertEqual("LICENSE_NOT_FOUND", evaluation.Code);
    AssertEqual("Activation failed. Please contact support.", evaluation.Message);
}

static async Task DisallowedEnvironmentIsDenied()
{
    var evaluation = await CreateStore().ValidateAsync(
        Request(environment: "staging"),
        DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
        CancellationToken.None);

    AssertFalse(evaluation.Valid);
    AssertEqual("LICENSE_ENVIRONMENT_NOT_ALLOWED", evaluation.Code);
}

static async Task SuspendedLicenseReturnsCentralMessage()
{
    var evaluation = await CreateStore().ValidateAsync(
        Request(clientId: "client-suspended", activationId: "client-suspended-pharmacloud-production"),
        DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
        CancellationToken.None);

    AssertFalse(evaluation.Valid);
    AssertEqual(LicenseStatuses.Suspended, evaluation.Status);
    AssertEqual("LICENSE_SUSPENDED", evaluation.Code);
    AssertEqual("Activation failed. Please contact billing.", evaluation.Message);
}

static async Task ExpiryUsesServerTimeOnly()
{
    var store = CreateStore();
    var request = Request(clientId: "client-expiring", activationId: "client-expiring-pharmacloud-production");

    var beforeExpiry = await store.ValidateAsync(
        request,
        DateTimeOffset.Parse("2026-06-30T23:59:59Z"),
        CancellationToken.None);
    AssertTrue(beforeExpiry.Valid);

    var afterExpiry = await store.ValidateAsync(
        request,
        DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        CancellationToken.None);
    AssertFalse(afterExpiry.Valid);
    AssertEqual(LicenseStatuses.Expired, afterExpiry.Status);
    AssertEqual("LICENSE_EXPIRED", afterExpiry.Code);
}

static async Task MissingRequiredFieldsAreRejected()
{
    var evaluation = await CreateStore().ValidateAsync(
        Request(clientId: ""),
        DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
        CancellationToken.None);

    AssertFalse(evaluation.Valid);
    AssertEqual("LICENSE_REQUEST_INVALID", evaluation.Code);
    AssertEqual("Activation failed. Missing client id.", evaluation.Message);
}

static Task TokenSignerEmitsVerifiableEs256Token()
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var privateKeyPem = key.ExportPkcs8PrivateKeyPem();
    var publicKeyPem = key.ExportSubjectPublicKeyInfoPem();
    var signer = CreateSigner(new SigningOptions
    {
        KeyId = "test-key-001",
        PrivateKeyPem = privateKeyPem
    });

    var token = signer.Sign(new LicenseTokenPayload(
        SchemaVersion: "1.0",
        Valid: true,
        Status: LicenseStatuses.Active,
        Code: "LICENSE_ACTIVE",
        Message: "License active.",
        ClientId: "client-a",
        ApplicationId: "pharmacloud",
        Environment: "production",
        ActivationId: "client-a-pharmacloud-production",
        InstallationId: "prod-installation",
        ServerTimeUtc: DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
        TokenValidUntilUtc: DateTimeOffset.Parse("2026-06-13T12:00:00Z"),
        Features: ["core"],
        Limits: new Dictionary<string, object?> { ["maxTenants"] = null }));

    var parts = token.Split('.');
    AssertEqual(3, parts.Length);

    var header = JsonDocument.Parse(Base64UrlDecode(parts[0])).RootElement;
    AssertEqual("ES256", header.GetProperty("alg").GetString());
    AssertEqual("JWT", header.GetProperty("typ").GetString());
    AssertEqual("test-key-001", header.GetProperty("kid").GetString());

    var payload = JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement;
    AssertTrue(payload.GetProperty("valid").GetBoolean());
    AssertEqual("client-a", payload.GetProperty("clientId").GetString());
    AssertEqual(JsonValueKind.Null, payload.GetProperty("limits").GetProperty("maxTenants").ValueKind);

    using var publicKey = ECDsa.Create();
    publicKey.ImportFromPem(publicKeyPem);
    var signedBytes = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
    var signature = Base64UrlDecode(parts[2]);
    AssertTrue(publicKey.VerifyData(
        signedBytes,
        signature,
        HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    AssertFalse(signer.IsEphemeral);
    return Task.CompletedTask;
}

static Task ReadinessIsDegradedForEphemeralKey()
{
    var signer = CreateSigner(new SigningOptions { KeyId = "ephemeral-test" });
    var healthCheck = new LicenseServiceHealthCheck(OptionsMonitor(new LicenseOptions
    {
        Licenses = [ActiveLicense()]
    }), signer);

    var result = healthCheck.CheckHealthAsync(new HealthCheckContext()).GetAwaiter().GetResult();

    AssertEqual(HealthStatus.Degraded, result.Status);
    AssertEqual("License service is using an ephemeral development signing key.", result.Description);
    return Task.CompletedTask;
}

static Task ReadinessIsHealthyForStableKey()
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var signer = CreateSigner(new SigningOptions
    {
        KeyId = "stable-test",
        PrivateKeyPem = key.ExportPkcs8PrivateKeyPem()
    });
    var healthCheck = new LicenseServiceHealthCheck(OptionsMonitor(new LicenseOptions
    {
        Licenses = [ActiveLicense()]
    }), signer);

    var result = healthCheck.CheckHealthAsync(new HealthCheckContext()).GetAwaiter().GetResult();

    AssertEqual(HealthStatus.Healthy, result.Status);
    AssertEqual("License service is ready.", result.Description);
    return Task.CompletedTask;
}

static ConfigurationLicenseStore CreateStore() =>
    new(OptionsMonitor(new LicenseOptions
    {
        DefaultInvalidMessage = "Activation failed. Please contact support.",
        Licenses =
        [
            ActiveLicense(),
            new ConfiguredLicense
            {
                ClientId = "client-suspended",
                ApplicationId = "pharmacloud",
                Status = LicenseStatuses.Suspended,
                ExpiresAtUtc = DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
                AllowedEnvironments = ["production"],
                AllowedActivationIds = ["client-suspended-pharmacloud-production"],
                AllowedInstallationIds = ["prod-installation"],
                Features = ["core"],
                Messages = new LicenseMessages
                {
                    Suspended = "Activation failed. Please contact billing."
                }
            },
            new ConfiguredLicense
            {
                ClientId = "client-expiring",
                ApplicationId = "pharmacloud",
                Status = LicenseStatuses.Active,
                ExpiresAtUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                AllowedEnvironments = ["production"],
                AllowedActivationIds = ["client-expiring-pharmacloud-production"],
                AllowedInstallationIds = ["prod-installation"],
                Features = ["core"]
            }
        ]
    }));

static ConfiguredLicense ActiveLicense() =>
    new()
    {
        ClientId = "client-a",
        ApplicationId = "pharmacloud",
        Status = LicenseStatuses.Active,
        ExpiresAtUtc = DateTimeOffset.Parse("2027-01-01T00:00:00Z"),
        AllowedEnvironments = ["production", "test"],
        AllowedActivationIds = ["client-a-pharmacloud-production", "client-a-pharmacloud-test"],
        AllowedInstallationIds = ["prod-installation", "test-installation"],
        Features = ["core", "reports"],
        Limits = new Dictionary<string, object?>
        {
            ["maxTenants"] = null,
            ["maxUsers"] = null
        },
        Messages = new LicenseMessages
        {
            Active = "License active.",
            Expired = "Activation failed. License has expired.",
            Suspended = "Activation failed. Please contact support.",
            Invalid = "Activation failed. Please contact support."
        }
    };

static LicenseValidationRequest Request(
    string clientId = "client-a",
    string applicationId = "pharmacloud",
    string environment = "production",
    string activationId = "client-a-pharmacloud-production",
    string installationId = "prod-installation") =>
    new(
        SchemaVersion: "1.0",
        ClientId: clientId,
        ApplicationId: applicationId,
        Environment: environment,
        ActivationId: activationId,
        InstallationId: installationId,
        AppVersion: "1.0.0",
        RequestId: Guid.NewGuid().ToString("N"),
        Extra: []);

static Es256LicenseTokenSigner CreateSigner(SigningOptions options)
{
    using var loggerFactory = LoggerFactory.Create(_ => { });
    return new Es256LicenseTokenSigner(
        Options.Create(options),
        loggerFactory.CreateLogger<Es256LicenseTokenSigner>());
}

static TestOptionsMonitor<T> OptionsMonitor<T>(T value) => new(value);

static byte[] Base64UrlDecode(string value)
{
    var padded = value.Replace('-', '+').Replace('_', '/');
    padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
    return Convert.FromBase64String(padded);
}

static void AssertTrue(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected condition to be true.");
    }
}

static void AssertFalse(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected condition to be false.");
    }
}

static void AssertNotNull(object? value)
{
    if (value is null)
    {
        throw new InvalidOperationException("Expected value to be non-null.");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
