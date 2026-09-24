namespace Vectify.Api.Preprocessing;

/// <summary>Resultado de validar los parámetros de preprocesamiento recibidos del cliente.</summary>
public sealed class PreprocessParameterValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public PreprocessParameters? Parameters { get; }

    private PreprocessParameterValidationResult(
        bool isValid, string? errorCode, string? errorMessage, PreprocessParameters? parameters)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Parameters = parameters;
    }

    public static PreprocessParameterValidationResult Success(PreprocessParameters parameters) =>
        new(true, null, null, parameters);

    public static PreprocessParameterValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
