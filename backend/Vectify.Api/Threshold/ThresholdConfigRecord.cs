namespace Vectify.Api.Threshold;

/// <summary>
/// Una versión guardada de configuración de threshold para una imagen, junto
/// con la referencia a la máscara ya generada (cacheada) para esos
/// parámetros y al preview preprocesado (M1-S03) del que se originó. Vive
/// solo en memoria en este sprint (mismo criterio que PreprocessConfigRecord).
/// </summary>
public sealed record ThresholdConfigRecord(
    Guid ProjectId,
    Guid ImageId,
    int Version,
    Guid MaskId,
    Guid SourcePreviewId,
    ThresholdParameters Parameters,
    string MaskStorageKey,
    string ContentType,
    int Width,
    int Height,
    ThresholdMetrics Metrics,
    DateTimeOffset CreatedAt);
