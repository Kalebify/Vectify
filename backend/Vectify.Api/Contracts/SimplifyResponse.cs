namespace Vectify.Api.Contracts;

/// <summary>
/// Estadísticas antes/después + % de reducción -- misma forma que devuelve el
/// motor Python (SimplifyMetrics). Reutiliza <see cref="VectorMetricsPayload"/>
/// (ya definido para VectorizeResponse) para "before"/"after".
/// </summary>
public sealed record SimplificationMetricsPayload(VectorMetricsPayload Before, VectorMetricsPayload After, double ReductionPercent);

/// <summary>
/// Respuesta de POST .../simplify/preview: SIN vectorId ni versión propios --
/// no se persistió nada (ver spec.md M1-S07, "Preview es reversible: cancelar
/// no deja rastro"). El SVG completo viaja en <c>Svg</c> para que React pueda
/// mostrar la comparación antes/después sin un round-trip adicional a la Web
/// API para pedir bytes que ni siquiera se guardaron.
/// </summary>
public sealed record SimplifyPreviewResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid SourceVectorId,
    string Svg,
    int Width,
    int Height,
    SimplificationMetricsPayload Metrics,
    string? Preset,
    double Tolerance);

/// <summary>
/// Respuesta de POST .../simplify/apply y GET .../simplifications/{id}: la
/// simplificación quedó persistida como una nueva SimplificationVersion
/// (nunca sobrescribe la anterior) -- ver spec.md M1-S07.
/// </summary>
public sealed record SimplifyResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid SimplificationId,
    string SvgUrl,
    Guid SourceVectorId,
    int Version,
    int Width,
    int Height,
    SimplificationMetricsPayload Metrics,
    string? Preset,
    double Tolerance,
    bool Cached);
