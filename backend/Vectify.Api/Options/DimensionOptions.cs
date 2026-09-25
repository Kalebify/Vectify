namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la etapa de dimensiones físicas en mm (M1-S09): rango
/// permitido de ancho/alto. spec.md no cuantifica "Rango válido de mm
/// (mínimo/máximo)" ("no bloqueante, el implementador elige valores
/// razonables... y los documenta como supuesto"); los valores de acá son ese
/// supuesto, documentado en el reporte del sprint: 1mm de mínimo (por debajo
/// de eso el valor deja de ser una medida físicamente útil para fabricación
/// con láser -- y evita división por valores cercanos a cero al despejar el
/// lado bloqueado del aspect ratio) y 1000mm de máximo (1 metro: compatible
/// con el área de trabajo de una cortadora láser de escritorio típica; una
/// pieza más grande que eso normalmente se resuelve con nesting/paneles,
/// fuera de alcance explícito de esta tarjeta). Rango INCLUSIVO en ambos
/// extremos (a diferencia de las tolerancias relativas de Simplification/
/// Check, que son "(0, max]": acá 1mm y 1000mm son valores límite legítimos
/// según spec.md, "Pruebas": "valores límite (mínimo/máximo permitido...)").
/// Se enlaza desde la sección "Dimensions".
/// </summary>
public sealed class DimensionOptions
{
    public const string SectionName = "Dimensions";

    public double MinMm { get; set; } = 1.0;
    public double MaxMm { get; set; } = 1000.0;
}
