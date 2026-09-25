namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../dimensions/apply. <see cref="SourceKind"/> ("vector"
/// o "simplification", case-insensitive) + <see cref="SourceId"/> indican
/// sobre qué SVG ya generado se aplican las dimensiones físicas -- mismo
/// criterio que Checking.CheckRequest (M1-S08). <see cref="LockAspectRatio"/>
/// (default true si se omite -- proporción bloqueada por defecto, ver
/// spec.md: "bloquear/desbloquear proporción... con lock (default)"):
/// bloqueada exige EXACTAMENTE uno de <see cref="WidthMm"/>/<see cref="HeightMm"/>
/// (el otro se calcula); desbloqueada exige AMBOS (permite deformar el
/// diseño explícitamente, ver Vectify.Api.Dimensioning.SvgDimensionWriter).
/// </summary>
public sealed record DimensionRequest(string SourceKind, Guid SourceId, double? WidthMm, double? HeightMm, bool? LockAspectRatio);
