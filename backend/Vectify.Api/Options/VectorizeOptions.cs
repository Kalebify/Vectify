namespace Vectify.Api.Options;

/// <summary>
/// Timeout HTTP para la llamada al motor Python al vectorizar. Se enlaza
/// desde la sección "Vectorize" de appsettings/variables de entorno. Mayor
/// que Vectorize:TimeoutSeconds del lado Python (vectorize_timeout_seconds,
/// services/python-engine/app/core/config.py) a propósito: así el timeout
/// tipado de Python (VectorizationTimeoutError, HTTP 504) llega primero, en
/// vez de que HttpClient corte la conexión antes y solo se vea "Unavailable"
/// del lado de Vectify.Api. spec.md no cuantifica ningún límite de tiempo
/// ("Ambigüedades detectadas"); valores documentados como supuesto en el
/// reporte del sprint.
/// </summary>
public sealed class VectorizeOptions
{
    public const string SectionName = "Vectorize";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Segunda barrera de tamaño para el SVG devuelto por el motor Python, del lado de
    /// Vectify.Api (además del límite que ya aplica Python al generar/sanitizar el SVG,
    /// Vectorize:MaxSvgOutputBytes en services/python-engine/app/core/config.py) -- defensa
    /// en profundidad, ver Defecto 3 de la ronda de QA sobre M1-S05/M1-S06: el cliente
    /// nunca debería confiar ciegamente en que Python ya aplicó su propio límite. 10 MB por
    /// default: bastante más grande que cualquier SVG razonable de un logo/silueta trazado.
    /// </summary>
    public int MaxSvgResponseBytes { get; set; } = 10 * 1024 * 1024;
}
