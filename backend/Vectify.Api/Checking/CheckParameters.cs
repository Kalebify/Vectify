namespace Vectify.Api.Checking;

/// <summary>
/// Tolerancias efectivas del Laser Checker de paths abiertos/duplicados
/// (M1-S08), YA resueltas (default de configuración si el cliente no mandó
/// un valor propio) y validadas contra el rango permitido -- ver
/// <see cref="ICheckParameterValidator"/>. Ambas son relativas a la diagonal
/// del bounding box del SVG de origen (mismo criterio que
/// Simplification.SimplificationParameters.EpsilonRatio, M1-S07), nunca un
/// valor absoluto en píxeles/unidades de pantalla -- así el análisis es
/// independiente del zoom del canvas. <see cref="SourceKind"/> es la
/// resolución de qué tipo de recurso de origen (VectorVersion o
/// SimplificationVersion) referencia <c>SourceId</c> en el request.
/// </summary>
public sealed record CheckParameters(double CloseGapRatio, double DuplicatePointRatio, CheckSourceKind SourceKind);
