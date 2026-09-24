namespace Vectify.Api.Options;

/// <summary>
/// Configuración para contactar al motor de vectorización Python/FastAPI.
/// Se enlaza desde la sección "PythonEngine" de appsettings/variables de entorno
/// (por ejemplo, PythonEngine__BaseUrl, PythonEngine__TimeoutSeconds).
/// </summary>
public sealed class PythonEngineOptions
{
    public const string SectionName = "PythonEngine";

    /// <summary>URL base del motor Python (por ejemplo, http://localhost:8001).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8001";

    /// <summary>Tiempo máximo de espera, en segundos, para las llamadas al motor Python.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}
