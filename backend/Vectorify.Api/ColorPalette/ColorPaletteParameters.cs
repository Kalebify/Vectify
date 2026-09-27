using System.Globalization;

namespace Vectorify.Api.ColorPalette;

/// <summary>
/// Parámetros efectivos de detección de paleta de colores (M2-S01/M2.1-S02):
/// tolerancia de fusión automática (distancia Lab, ver
/// services/python-engine/app/core/color_palette_pipeline.py), número
/// objetivo (límite superior opcional) de colores, y (M2.1-S02)
/// <see cref="TinyAreaRatio"/> -- umbral de "grupo diminuto" (relativo a los
/// píxeles relevantes de la imagen) para fusionar automáticamente hacia su
/// vecino más cercano los grupos que la explosión de colores por
/// antialiasing genera (ver auditoría M2.1-S01 y
/// app.core.color_palette_pipeline._merge_tiny_groups_into_nearest).
/// </summary>
public sealed record ColorPaletteParameters(double Tolerance, int? MaxColors, double TinyAreaRatio)
{
    /// <summary>Clave estable para deduplicar/cachear detecciones -- ver ColorPaletteVersion.</summary>
    public string ToCacheKey() =>
        $"tolerance={Tolerance.ToString("F6", CultureInfo.InvariantCulture)};" +
        $"maxColors={MaxColors?.ToString(CultureInfo.InvariantCulture) ?? "none"};" +
        $"tinyAreaRatio={TinyAreaRatio.ToString("F6", CultureInfo.InvariantCulture)}";
}
