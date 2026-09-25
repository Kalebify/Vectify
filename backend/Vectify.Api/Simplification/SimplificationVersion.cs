namespace Vectify.Api.Simplification;

/// <summary>
/// Una versión guardada de simplificación de nodos para una imagen, junto con
/// la referencia al SVG ya generado (cacheado) para ese SVG de origen +
/// parámetros, y al <c>VectorId</c> (Vectorization/VectorVersion, M1-S05) del
/// que se originó. Módulo propio y paralelo a Vectorization -- no se mezcla
/// con VectorVersion (ver spec.md M1-S07: "es una etapa propia, aunque opera
/// sobre una VectorVersion ya creada"), pero sigue exactamente el mismo
/// patrón de historial versionado (nunca se sobrescribe, cache-hit avanza la
/// versión) que VectorVersion/ThresholdConfigRecord/PreprocessConfigRecord.
/// </summary>
public sealed record SimplificationVersion(
    Guid ProjectId,
    Guid ImageId,
    int Version,
    Guid SimplificationId,
    Guid SourceVectorId,
    SimplificationParameters Parameters,
    string SvgStorageKey,
    string ContentType,
    int Width,
    int Height,
    SimplificationMetrics Metrics,
    DateTimeOffset CreatedAt);
