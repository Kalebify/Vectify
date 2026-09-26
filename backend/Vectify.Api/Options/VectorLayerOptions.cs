namespace Vectify.Api.Options;

/// <summary>
/// Timeout HTTP para la llamada al motor Python al generar el conjunto
/// completo de capas de una paleta confirmada (POST /api/v1/vectorize-layers,
/// M2-S02). Más alto que Vectorize:TimeoutSeconds porque una única llamada
/// vectoriza N máscaras server-side (ver spec.md, "Ambigüedades
/// detectadas") -- mismo criterio de holgura que VectorizeOptions/
/// SimplificationOptions. Se enlaza desde la sección "VectorLayer".
/// </summary>
public sealed class VectorLayerOptions
{
    public const string SectionName = "VectorLayer";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Segunda barrera de tamaño para cada SVG de capa devuelto por el motor
    /// Python, del lado de Vectify.Api -- mismo criterio de defensa en
    /// profundidad que Vectorize:MaxSvgResponseBytes.
    /// </summary>
    public int MaxSvgResponseBytes { get; set; } = 10 * 1024 * 1024;
}
