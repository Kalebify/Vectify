namespace Vectify.Api.Threshold;

/// <summary>Resultado de validar los parámetros de threshold recibidos del cliente.</summary>
public sealed class ThresholdParameterValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public ThresholdParameters? Parameters { get; }

    private ThresholdParameterValidationResult(
        bool isValid, string? errorCode, string? errorMessage, ThresholdParameters? parameters)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Parameters = parameters;
    }

    public static ThresholdParameterValidationResult Success(ThresholdParameters parameters) =>
        new(true, null, null, parameters);

    public static ThresholdParameterValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
