namespace Vectify.Api.Options;

/// <summary>
/// Configuración de detección de paleta de colores (M2-S01): timeout HTTP
/// hacia el motor Python y rangos permitidos de tolerancia/número objetivo
/// de colores. spec.md no cuantifica ninguno de estos valores
/// ("Ambigüedades detectadas": "el implementador decide y documenta"); los
/// valores de acá son ese supuesto, documentado en el reporte del sprint,
/// alineados 1:1 con services/python-engine/app/core/config.py
/// (color_palette_default_tolerance, etc.) -- defensa en profundidad, mismo
/// criterio que SimplificationOptions/CheckOptions. Se enlaza desde la
/// sección "ColorPalette".
/// </summary>
public sealed class ColorPaletteOptions
{
    public const string SectionName = "ColorPalette";

    public int TimeoutSeconds { get; set; } = 25;

    public double DefaultTolerance { get; set; } = 12.0;
    public double MinTolerance { get; set; } = 0.0;
    public double MaxTolerance { get; set; } = 100.0;

    public int MinColors { get; set; } = 1;
    public int MaxColorsUpperBound { get; set; } = 64;
}
