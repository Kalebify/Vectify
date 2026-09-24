namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST /api/v1/projects/{projectId}/images/{imageId}/preview.
/// Ver spec.md M1-S03, sección "Contratos": "Entrada: imageId + parámetros
/// versionados" (imageId viaja en la ruta; acá van los parámetros del pipeline).
/// </summary>
public sealed record PreprocessRequest(bool Grayscale, double Contrast, int Brightness, int Denoise);
