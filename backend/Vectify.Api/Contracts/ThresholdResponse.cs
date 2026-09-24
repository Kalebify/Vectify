namespace Vectify.Api.Contracts;

/// <summary>
/// Respuesta de POST/GET de la máscara de threshold. Ver spec.md M1-S04,
/// sección "Contratos": salida = mask preview + métricas + parámetros
/// efectivos.
/// </summary>
public sealed record ThresholdResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid MaskId,
    string MaskUrl,
    Guid SourcePreviewId,
    int Version,
    int Width,
    int Height,
    ThresholdParametersPayload EffectiveParams,
    ThresholdMetricsPayload Metrics,
    bool Cached);

public sealed record ThresholdParametersPayload(int Value, bool Invert);

/// <summary>
/// WarningCode/WarningMessage son null cuando la máscara no está en un
/// extremo (ni "casi vacía" ni "casi llena"); si lo está, React los muestra
/// como advertencia, no como error (ver spec.md, criterios de aceptación).
/// </summary>
public sealed record ThresholdMetricsPayload(
    double ForegroundPercent,
    double BackgroundPercent,
    bool IsNearEmpty,
    bool IsNearFull,
    string? WarningCode,
    string? WarningMessage);
