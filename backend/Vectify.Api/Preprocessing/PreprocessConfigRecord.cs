namespace Vectify.Api.Preprocessing;

/// <summary>
/// Una versión guardada de configuración de preprocesamiento para una imagen,
/// junto con la referencia al preview ya generado (cacheado) para esos
/// parámetros. Vive solo en memoria en este sprint (mismo criterio que
/// ProjectRecord — ver Vectify.Api.Projects.ProjectRecord).
/// </summary>
public sealed record PreprocessConfigRecord(
    Guid ProjectId,
    Guid ImageId,
    int Version,
    Guid PreviewId,
    PreprocessParameters Parameters,
    string PreviewStorageKey,
    string ContentType,
    int Width,
    int Height,
    int OriginalWidth,
    int OriginalHeight,
    PreprocessMetrics Metrics,
    DateTimeOffset CreatedAt);
