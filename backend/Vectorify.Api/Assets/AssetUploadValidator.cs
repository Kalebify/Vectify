using Microsoft.Extensions.Options;
using Vectorify.Api.Options;

namespace Vectorify.Api.Assets;

/// <summary>
/// Implementación de <see cref="IAssetUploadValidator"/>: valida archivo vacío/ausente,
/// tamaño máximo (<see cref="AssetOptions.MaxFileSizeBytes"/>) y MIME type contra la lista
/// permitida (<see cref="AssetOptions.GetAllowedContentTypes"/>), exigiendo además que el
/// MIME type tenga una extensión mapeada en <see cref="AssetKeyFactory"/> (si no la tiene,
/// no hay forma de derivar la clave de storage).
///
/// Deliberadamente MÁS liviano que <see cref="Vectorify.Api.Validation.ImageUploadValidator"/>
/// del flujo clásico (que además verifica la firma binaria y decodifica la imagen completa
/// con ImageSharp): un Asset no es necesariamente una imagen raster decodificable --
/// <c>image/svg+xml</c> es texto/XML, no algo que ImageSharp pueda abrir. Ver spec.md,
/// "Seguridad" ("Validar MIME contra una lista permitida") e IMPL.md para el razonamiento
/// completo de por qué no se reusa <see cref="Vectorify.Api.Validation.IImageUploadValidator"/>
/// tal cual.
/// </summary>
public sealed class AssetUploadValidator : IAssetUploadValidator
{
    private readonly AssetOptions _options;

    public AssetUploadValidator(IOptions<AssetOptions> options)
    {
        _options = options.Value;
    }

    public AssetValidationResult Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return AssetValidationResult.Failure(
                "empty_file",
                "No se recibió ningún archivo, o el archivo está vacío.");
        }

        if (file.Length > _options.MaxFileSizeBytes)
        {
            var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
            return AssetValidationResult.Failure(
                "file_too_large",
                $"El archivo supera el tamaño máximo permitido ({maxMb:0.#} MB).");
        }

        var contentType = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var allowedContentTypes = _options.GetAllowedContentTypes();

        if (!allowedContentTypes.Contains(contentType) || !AssetKeyFactory.TryGetExtension(contentType, out _))
        {
            return AssetValidationResult.Failure(
                "unsupported_format",
                "Formato no soportado. Solo se aceptan imágenes PNG, JPG/JPEG, WEBP o SVG.");
        }

        return AssetValidationResult.Success(contentType);
    }
}
