namespace Vectify.Api.Threshold;

/// <summary>
/// Métricas de porcentaje foreground/background del threshold, más la
/// clasificación de advertencia calculada por Vectify.Api (no por Python)
/// contra los umbrales configurados (Threshold:NearEmptyMaxForegroundPercent /
/// NearFullMinForegroundPercent). Ver spec.md M1-S04: "Python calcula métricas
/// de porcentaje..."; "casos extremos se comunican como advertencia, no error
/// silencioso". WarningCode/WarningMessage son null cuando la máscara está en
/// un rango razonable (ni casi vacía ni casi llena).
/// </summary>
public sealed record ThresholdMetrics(
    double ForegroundPercent,
    double BackgroundPercent,
    bool IsNearEmpty,
    bool IsNearFull,
    string? WarningCode,
    string? WarningMessage);
