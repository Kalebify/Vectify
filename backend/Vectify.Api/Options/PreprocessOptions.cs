namespace Vectify.Api.Options;

/// <summary>
/// Rangos válidos de los parámetros del pipeline de preprocesamiento y el timeout
/// para llamar al motor Python. Se enlaza desde la sección "Preprocess" de
/// appsettings/variables de entorno (por ejemplo, Preprocess__MaxContrast).
/// spec.md no cuantifica estos valores ("Ambigüedades detectadas" en spec.md);
/// son un supuesto documentado en el reporte del sprint y deben coincidir con
/// los que valida PreprocessParams del lado Python (services/python-engine/app/
/// models/schemas.py) — la Web API es la primera línea de validación, Python
/// vuelve a validar en defensa en profundidad.
/// </summary>
public sealed class PreprocessOptions
{
    public const string SectionName = "Preprocess";

    public double MinContrast { get; set; } = 0.5;
    public double MaxContrast { get; set; } = 3.0;

    public int MinBrightness { get; set; } = -100;
    public int MaxBrightness { get; set; } = 100;

    public int MinDenoise { get; set; } = 0;
    public int MaxDenoise { get; set; } = 10;

    /// <summary>
    /// Timeout, en segundos, para la llamada al motor Python al generar un preview.
    /// Más alto que PythonEngine:TimeoutSeconds (pensado para el chequeo de salud,
    /// mucho más liviano) porque decodificar/transformar/codificar una imagen con
    /// OpenCV puede tardar más que un simple GET /health.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 20;
}
