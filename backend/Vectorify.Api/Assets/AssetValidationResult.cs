namespace Vectorify.Api.Assets;

/// <summary>Resultado de validar un archivo candidato a Asset (M2.2-S04). Mismo patrón que <see cref="Vectorify.Api.Validation.ImageValidationResult"/> del flujo clásico.</summary>
public sealed class AssetValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public string? ContentType { get; }

    private AssetValidationResult(bool isValid, string? errorCode, string? errorMessage, string? contentType)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ContentType = contentType;
    }

    public static AssetValidationResult Success(string contentType) => new(true, null, null, contentType);

    public static AssetValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
