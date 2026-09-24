namespace Vectify.Api.Validation;

/// <summary>Resultado de validar un archivo candidato a original de proyecto.</summary>
public sealed class ImageValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public string? ContentType { get; }
    public string? Extension { get; }

    private ImageValidationResult(bool isValid, string? errorCode, string? errorMessage, string? contentType, string? extension)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ContentType = contentType;
        Extension = extension;
    }

    public static ImageValidationResult Success(string contentType, string extension) =>
        new(true, null, null, contentType, extension);

    public static ImageValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null, null);
}
