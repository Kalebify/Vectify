namespace Vectify.Api.Dimensioning;

/// <summary>Resultado del primer paso de validación (estructural, sin conocer aún el SVG de origen) -- ver <see cref="IDimensionParameterValidator.ValidateRequest"/>.</summary>
public sealed class DimensionRequestValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public DimensionRequestParameters? Parameters { get; }

    private DimensionRequestValidationResult(
        bool isValid, string? errorCode, string? errorMessage, DimensionRequestParameters? parameters)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Parameters = parameters;
    }

    public static DimensionRequestValidationResult Success(DimensionRequestParameters parameters) =>
        new(true, null, null, parameters);

    public static DimensionRequestValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
