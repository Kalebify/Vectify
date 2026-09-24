namespace Vectify.Api.Threshold;

/// <summary>
/// Métricas crudas de porcentaje foreground/background devueltas por el
/// motor Python, antes de que Vectify.Api las clasifique contra los umbrales
/// configurados (ver <see cref="ThresholdMetrics"/> y ThresholdService.EvaluateMetrics).
/// </summary>
public sealed record ThresholdRawMetrics(double ForegroundPercent, double BackgroundPercent);
