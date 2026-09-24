namespace Vectify.Api.Contracts;

/// <summary>
/// Metadatos tipados devueltos por POST /api/v1/projects tras una carga exitosa
/// (o por GET al recuperar el proyecto). Width/Height son null cuando el formato
/// no permitió leer las dimensiones ("cuando estén disponibles" según spec.md).
/// Status: "uploaded" (único valor de este sprint; el original todavía no se procesa).
/// </summary>
public sealed record UploadImageResponse(
    Guid ProjectId,
    Guid ImageId,
    string Filename,
    string MimeType,
    long Bytes,
    int? Width,
    int? Height,
    string Status);
