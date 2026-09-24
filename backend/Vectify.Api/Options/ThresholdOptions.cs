namespace Vectify.Api.Options;

/// <summary>
/// Rango válido del valor de umbral, el timeout para llamar al motor Python y
/// los umbrales de porcentaje foreground que clasifican una máscara como
/// "casi vacía"/"casi llena" (advertencia, no error). Se enlaza desde la
/// sección "Threshold" de appsettings/variables de entorno. spec.md no
/// cuantifica ninguno de estos valores ("Ambigüedades detectadas" en spec.md);
/// son un supuesto documentado en el reporte del sprint y deben coincidir con
/// los que valida ThresholdParams del lado Python
/// (services/python-engine/app/models/schemas.py) -- la Web API es la primera
/// línea de validación de rango, Python vuelve a validar en defensa en
/// profundidad (la clasificación de advertencia es exclusiva de Vectify.Api).
/// </summary>
public sealed class ThresholdOptions
{
    public const string SectionName = "Threshold";

    public int MinValue { get; set; } = 0;
    public int MaxValue { get; set; } = 255;

    /// <summary>
    /// Umbral (en % de píxeles foreground de la máscara resultante) por
    /// debajo o igual al cual se considera "casi vacía" y se comunica como
    /// advertencia. 2% es un supuesto documentado en el reporte del sprint.
    /// </summary>
    public double NearEmptyMaxForegroundPercent { get; set; } = 2.0;

    /// <summary>
    /// Umbral (en % de píxeles foreground) por encima o igual al cual la
    /// máscara se considera "casi llena". Mismo criterio que
    /// NearEmptyMaxForegroundPercent.
    /// </summary>
    public double NearFullMinForegroundPercent { get; set; } = 98.0;

    /// <summary>
    /// Timeout, en segundos, para la llamada al motor Python al generar una
    /// máscara. Mismo criterio que Preprocess:TimeoutSeconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 20;
}
