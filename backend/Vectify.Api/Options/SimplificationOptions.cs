namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la etapa de simplificación de nodos (M1-S07): timeout
/// HTTP hacia el motor Python, segunda barrera de tamaño de respuesta
/// (defensa en profundidad, mismo criterio que Vectorize:MaxSvgResponseBytes)
/// y los presets Bajo/Medio/Alto que ve el usuario en React, expresados como
/// epsilon de Douglas-Peucker RELATIVO a la diagonal del bounding box del SVG
/// de origen (no un valor absoluto en píxeles, para que escale con el tamaño
/// del diseño -- ver app.core.simplification_pipeline del lado Python).
///
/// spec.md no cuantifica ninguno de estos valores ("Valores numéricos
/// concretos de los presets Bajo/Medio/Alto no están cuantificados -- no
/// bloqueante, el implementador elige y documenta como supuesto"); los
/// valores de acá son ese supuesto, documentado en el reporte del sprint:
/// Bajo = 0.15% de la diagonal (cambios mínimos, conserva casi todo el
/// detalle), Medio = 0.4% (reducción moderada, el punto de partida
/// recomendado), Alto = 1.2% (reducción agresiva, para diseños con
/// muchísimos nodos donde el detalle fino importa menos que el tamaño del
/// archivo). Se enlaza desde la sección "Simplification".
/// </summary>
public sealed class SimplificationOptions
{
    public const string SectionName = "Simplification";

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Segunda barrera de tamaño para el SVG devuelto por el motor Python, del lado de
    /// Vectify.Api (además del límite que ya aplica Python, Vectorize:MaxSvgOutputBytes,
    /// reutilizado como límite de entrada/salida de esta etapa) -- mismo criterio que
    /// VectorizeOptions.MaxSvgResponseBytes.
    /// </summary>
    public int MaxSvgResponseBytes { get; set; } = 10 * 1024 * 1024;

    public double LowEpsilonRatio { get; set; } = 0.0015;
    public double MediumEpsilonRatio { get; set; } = 0.004;
    public double HighEpsilonRatio { get; set; } = 0.012;

    /// <summary>
    /// Rango permitido si el cliente expone una tolerancia numérica custom en
    /// vez de un preset (criterio de aceptación de spec.md M1-S07: "validar
    /// tolerancia ... no solo el preset sino también si se expone un valor
    /// numérico custom"). Mismo rango que acepta SimplifyParams.epsilon_ratio
    /// del lado Python (services/python-engine/app/models/schemas.py):
    /// (0, 0.5] -- documentado como supuesto, sin cuantificar por spec.md.
    /// </summary>
    public double MinCustomTolerance { get; set; } = 0.0;
    public double MaxCustomTolerance { get; set; } = 0.5;
}
