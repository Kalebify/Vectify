namespace Vectify.Api.Checking;

/// <summary>Resultado de validar los parámetros del Laser Checker recibidos del cliente.</summary>
public sealed class CheckParameterValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public CheckParameters? Parameters { get; }

    private CheckParameterValidationResult(bool isValid, string? errorCode, string? errorMessage, CheckParameters? parameters)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Parameters = parameters;
    }

    public static CheckParameterValidationResult Success(CheckParameters parameters) =>
        new(true, null, null, parameters);

    public static CheckParameterValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
