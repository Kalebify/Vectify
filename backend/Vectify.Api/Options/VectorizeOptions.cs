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
}
