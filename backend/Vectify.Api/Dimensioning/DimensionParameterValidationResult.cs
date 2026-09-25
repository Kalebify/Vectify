namespace Vectify.Api.Dimensioning;

/// <summary>Resultado del segundo paso de validación (dimensiones YA resueltas contra el SVG de origen) -- ver <see cref="IDimensionParameterValidator.ResolveDimensions"/>.</summary>
public sealed class DimensionParameterValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public DimensionParameters? Parameters { get; }

    private DimensionParameterValidationResult(
        bool isValid, string? errorCode, string? errorMessage, DimensionParameters? parameters)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Parameters = parameters;
    }

    public static DimensionParameterValidationResult Success(DimensionParameters parameters) =>
        new(true, null, null, parameters);

    public static DimensionParameterValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
