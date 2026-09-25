namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../check. <see cref="SourceKind"/> ("vector" o
/// "simplification", case-insensitive) + <see cref="SourceId"/> indican
/// sobre qué SVG ya generado corre el análisis -- el spec no aclara si el
/// checker opera sobre una VectorVersion (M1-S05) o también sobre una
/// SimplificationVersion (M1-S07); se decidió aceptar ambas (ver
/// Vectify.Api.Checking.CheckSourceKind). <see cref="CloseGapRatio"/>/
/// <see cref="DuplicatePointRatio"/> son opcionales (si se omiten, se usan
/// los valores configurados por default, ver Options.CheckOptions) --
/// tolerancias relativas a la diagonal del SVG, en unidades del modelo, NO
/// píxeles de pantalla ni relativas al zoom del canvas.
/// </summary>
public sealed record CheckRequest(string SourceKind, Guid SourceId, double? CloseGapRatio, double? DuplicatePointRatio);
