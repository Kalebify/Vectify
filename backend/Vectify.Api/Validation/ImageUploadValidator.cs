using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Validation;

/// <summary>
/// Implementación de <see cref="IImageUploadValidator"/>: valida, en orden,
/// archivo vacío/ausente, tamaño máximo, MIME type + extensión permitidos y
/// finalmente la firma binaria del contenido (detecta corrupción o un
/// Content-Type/extensión que no coincide con los bytes reales).
/// </summary>
public sealed class ImageUploadValidator : IImageUploadValidator
{
    private static readonly IReadOnlyDictionary<string, string[]> ExtensionsByContentType =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = [".png"],
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/webp"] = [".webp"],
        };

    private readonly UploadOptions _options;

    public ImageUploadValidator(IOptions<UploadOptions> options)
    {
        _options = options.Value;
    }

    public ImageValidationResult Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return ImageValidationResult.Failure(
                "empty_file",
                "No se recibió ningún archivo, o el archivo está vacío.");
        }

        if (file.Length > _options.MaxFileSizeBytes)
        {
            var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
            return ImageValidationResult.Failure(
                "file_too_large",
                $"El archivo supera el tamaño máximo permitido ({maxMb:0.#} MB).");
        }

        var contentType = file.ContentType.Trim().ToLowerInvariant();
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowedContentTypes = _options.GetAllowedContentTypes();

        var isKnownContentType = allowedContentTypes.Contains(contentType)
            && ExtensionsByContentType.TryGetValue(contentType, out var validExtensions)
            && validExtensions.Contains(extension);

        if (!isKnownContentType)
        {
            return ImageValidationResult.Failure(
                "unsupported_format",
                "Formato no soportado. Solo se aceptan imágenes PNG, JPG/JPEG o WEBP.");
        }

        using var stream = file.OpenReadStream();
        if (!ImageSignature.Matches(stream, contentType))
        {
            return ImageValidationResult.Failure(
                "corrupt_file",
                "El archivo parece estar corrupto: su contenido no coincide con el formato declarado.");
        }

        return ImageValidationResult.Success(contentType, extension);
    }
}
