namespace Vectify.Api.Simplification;

/// <summary>Resultado de validar los parámetros de simplificación recibidos del cliente.</summary>
public sealed class SimplificationParameterValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public SimplificationParameters? Parameters { get; }

    private SimplificationParameterValidationResult(
        bool isValid, string? errorCode, string? errorMessage, SimplificationParameters? parameters)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Parameters = parameters;
    }

    public static SimplificationParameterValidationResult Success(SimplificationParameters parameters) =>
        new(true, null, null, parameters);

    public static SimplificationParameterValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
