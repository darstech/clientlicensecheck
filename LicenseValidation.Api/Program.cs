using System.Diagnostics;
using System.Text.Json;
using LicenseValidation.Api.Licensing;
using LicenseValidation.Api.Signing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Configuration.AddJsonFile("licenses.json", optional: true, reloadOnChange: true);

builder.Services.Configure<LicenseOptions>(builder.Configuration.GetSection(LicenseOptions.SectionName));
builder.Services.Configure<SigningOptions>(builder.Configuration.GetSection(SigningOptions.SectionName));
builder.Services.AddSingleton<ILicenseStore, ConfigurationLicenseStore>();
builder.Services.AddSingleton<ILicenseTokenSigner, Es256LicenseTokenSigner>();
builder.Services.AddHealthChecks().AddCheck<LicenseServiceHealthCheck>("license-service");

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "Client License Validation API",
    version = "1.0",
    status = "running",
    serverTimeUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/health/live", () =>
{
    var started = Stopwatch.GetTimestamp();
    return Results.Ok(new
    {
        status = "Healthy",
        serverTimeUtc = DateTimeOffset.UtcNow,
        responseTimeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds
    });
});

app.MapGet("/health/ready", async (HealthCheckService healthCheckService, CancellationToken cancellationToken) =>
{
    var started = Stopwatch.GetTimestamp();
    var report = await healthCheckService.CheckHealthAsync(cancellationToken);

    return Results.Json(new
    {
        status = report.Status.ToString(),
        serverTimeUtc = DateTimeOffset.UtcNow,
        responseTimeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            durationMs = entry.Value.Duration.TotalMilliseconds,
            data = entry.Value.Data.ToDictionary(
                item => item.Key,
                item => item.Value?.ToString())
        })
    }, statusCode: report.Status == HealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK);
});

app.MapPost("/api/v1/license/validate", async (
    LicenseValidationRequest request,
    ILicenseStore licenseStore,
    ILicenseTokenSigner tokenSigner,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var started = Stopwatch.GetTimestamp();
    var logger = loggerFactory.CreateLogger("LicenseValidation");
    var now = DateTimeOffset.UtcNow;

    var evaluation = await licenseStore.ValidateAsync(request, now, cancellationToken);
    var tokenValidUntilUtc = now.AddDays(1);
    var response = LicenseValidationResponse.FromEvaluation(
        evaluation,
        now,
        tokenValidUntilUtc,
        Stopwatch.GetElapsedTime(started).TotalMilliseconds);

    response = response with
    {
        Token = tokenSigner.Sign(new LicenseTokenPayload(
            SchemaVersion: "1.0",
            Valid: response.Valid,
            Status: response.Status,
            Code: response.Code,
            Message: response.Message,
            ClientId: request.ClientId,
            ApplicationId: request.ApplicationId,
            Environment: request.Environment,
            ActivationId: request.ActivationId,
            InstallationId: request.InstallationId,
            ServerTimeUtc: now,
            TokenValidUntilUtc: tokenValidUntilUtc,
            Features: response.Features,
            Limits: response.Limits))
    };

    logger.LogInformation(
        "License validation completed clientId={ClientId} applicationId={ApplicationId} environment={Environment} activationId={ActivationId} installationId={InstallationId} valid={Valid} status={Status} code={Code} responseTimeMs={ResponseTimeMs}",
        request.ClientId,
        request.ApplicationId,
        request.Environment,
        request.ActivationId,
        request.InstallationId,
        response.Valid,
        response.Status,
        response.Code,
        response.ResponseTimeMs);

    return Results.Ok(response);
});

app.Run();

namespace LicenseValidation.Api.Licensing
{
    public sealed record LicenseValidationRequest(
        string SchemaVersion,
        string ClientId,
        string ApplicationId,
        string Environment,
        string ActivationId,
        string InstallationId,
        string? AppVersion,
        string? RequestId,
        Dictionary<string, JsonElement>? Extra);

    public sealed record LicenseValidationResponse(
        bool Valid,
        string Status,
        string Code,
        string Message,
        DateTimeOffset ServerTimeUtc,
        DateTimeOffset TokenValidUntilUtc,
        IReadOnlyList<string> Features,
        IReadOnlyDictionary<string, object?> Limits,
        double ResponseTimeMs,
        string? Token)
    {
        public static LicenseValidationResponse FromEvaluation(
            LicenseEvaluation evaluation,
            DateTimeOffset serverTimeUtc,
            DateTimeOffset tokenValidUntilUtc,
            double responseTimeMs) =>
            new(
                evaluation.Valid,
                evaluation.Status,
                evaluation.Code,
                evaluation.Message,
                serverTimeUtc,
                tokenValidUntilUtc,
                evaluation.Features,
                evaluation.Limits,
                responseTimeMs,
                Token: null);
    }

    public sealed record LicenseEvaluation(
        bool Valid,
        string Status,
        string Code,
        string Message,
        IReadOnlyList<string> Features,
        IReadOnlyDictionary<string, object?> Limits);
}
