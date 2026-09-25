using System.Globalization;

namespace Vectify.Api.ColorPalette;

/// <summary>
/// Parámetros efectivos de detección de paleta de colores (M2-S01):
/// tolerancia de fusión automática (distancia Lab, ver
/// services/python-engine/app/core/color_palette_pipeline.py) y número
/// objetivo (límite superior opcional) de colores.
/// </summary>
public sealed record ColorPaletteParameters(double Tolerance, int? MaxColors)
{
    /// <summary>Clave estable para deduplicar/cachear detecciones -- ver ColorPaletteVersion.</summary>
    public string ToCacheKey() =>
        $"tolerance={Tolerance.ToString("F6", CultureInfo.InvariantCulture)};maxColors={MaxColors?.ToString(CultureInfo.InvariantCulture) ?? "none"}";
}
