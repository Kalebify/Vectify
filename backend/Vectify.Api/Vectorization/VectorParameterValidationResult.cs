namespace Vectify.Api.Vectorization;

/// <summary>Resultado de validar los parámetros de vectorización recibidos del cliente.</summary>
public sealed class VectorParameterValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public VectorParameters? Parameters { get; }

    private VectorParameterValidationResult(
        bool isValid, string? errorCode, string? errorMessage, VectorParameters? parameters)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Parameters = parameters;
    }

    public static VectorParameterValidationResult Success(VectorParameters parameters) =>
        new(true, null, null, parameters);

    public static VectorParameterValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
