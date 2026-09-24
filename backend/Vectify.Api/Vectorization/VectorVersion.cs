namespace Vectify.Api.Vectorization;

/// <summary>
/// Una versión guardada de vectorización para una imagen, junto con la
/// referencia al SVG ya generado (cacheado) para esa máscara de origen +
/// parámetros, y a la máscara B/N (M1-S04) de la que se originó. Vive solo en
/// memoria en este sprint (mismo criterio que ThresholdConfigRecord/
/// PreprocessConfigRecord). Nombrado `VectorVersion` (no `VectorConfigRecord`)
/// porque así lo pide el cuerpo de la tarjeta M1-S05 explícitamente: "...
/// persistir una VectorVersion y devolver metadatos".
/// </summary>
public sealed record VectorVersion(
    Guid ProjectId,
    Guid ImageId,
    int Version,
    Guid VectorId,
    Guid SourceMaskId,
    VectorParameters Parameters,
    string SvgStorageKey,
    string ContentType,
    int Width,
    int Height,
    VectorMetrics Metrics,
    DateTimeOffset CreatedAt);
