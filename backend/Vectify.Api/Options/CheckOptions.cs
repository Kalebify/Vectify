namespace Vectify.Api.Options;

/// <summary>
/// Configuración del Laser Checker de paths abiertos/duplicados (M1-S08):
/// timeout HTTP hacia el motor Python, y las tolerancias relativas a la
/// diagonal del bounding box del SVG de origen (no un valor absoluto en
/// píxeles, para que el análisis sea independiente del tamaño del diseño y
/// del zoom del canvas -- ver app.core.path_checker del lado Python).
///
/// spec.md no cuantifica "Valor(es) de tolerancia por defecto" ("no
/// bloqueante, el implementador elige y documenta"); los valores de acá son
/// ese supuesto, documentado en el reporte del sprint: 0.5% de la diagonal
/// para "debería estar cerrado" (un gap moderado puede seguir siendo
/// legítimo -- ej. una forma tipo "C" con abertura deliberada), 0.2% para
/// "casi-duplicado" (más estricto: dos subpaths casi idénticos son una
/// señal más fuerte de un problema real de diseño que un gap de cierre
/// moderado). Se enlaza desde la sección "Check". Mismo rango permitido
/// (0, 0.5] que valida el motor Python (CheckParams de
/// services/python-engine/app/models/schemas.py) para ambas tolerancias.
/// </summary>
public sealed class CheckOptions
{
    public const string SectionName = "Check";

    public int TimeoutSeconds { get; set; } = 20;

    public double DefaultCloseGapRatio { get; set; } = 0.005;
    public double DefaultDuplicatePointRatio { get; set; } = 0.002;

    public double MinCloseGapRatio { get; set; } = 0.0;
    public double MaxCloseGapRatio { get; set; } = 0.5;
    public double MinDuplicatePointRatio { get; set; } = 0.0;
    public double MaxDuplicatePointRatio { get; set; } = 0.5;
}
