namespace LicenseValidation.Api.Licensing;

public interface ILicenseStore
{
    Task<LicenseEvaluation> ValidateAsync(
        LicenseValidationRequest request,
        DateTimeOffset serverTimeUtc,
        CancellationToken cancellationToken);
}
