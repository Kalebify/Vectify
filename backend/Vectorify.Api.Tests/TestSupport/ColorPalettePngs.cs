using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Vectorify.Api.Tests.TestSupport;

/// <summary>
/// Genera PNGs reales y válidos en memoria (máscaras binarias y previews) de
/// dimensiones exactas, para tests de ColorPaletteService/MaskCompositor que
/// necesitan que las máscaras coincidan con el ancho/alto declarado (a
/// diferencia de TestSupport.SampleImages.ValidPng1x1, que es solo 1x1).
/// </summary>
internal static class ColorPalettePngs
{
    /// <summary>Máscara L8 sólida: 255 (blanco) en toda la imagen si `filled`, 0 (negro) en caso contrario.</summary>
    public static byte[] SolidMask(int width, int height, bool filled = true)
    {
        using var image = new Image<L8>(width, height, new L8(filled ? (byte)255 : (byte)0));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>Máscara L8 con la mitad izquierda (columnas 0..width/2) en blanco (255) y el resto en negro (0).</summary>
    public static byte[] LeftHalfMask(int width, int height)
    {
        using var image = new Image<L8>(width, height, new L8(0));
        var half = width / 2;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < half; x++)
            {
                image[x, y] = new L8(255);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>Máscara L8 con la mitad derecha (columnas width/2..width) en blanco (255) y el resto en negro (0).</summary>
    public static byte[] RightHalfMask(int width, int height)
    {
        using var image = new Image<L8>(width, height, new L8(0));
        var half = width / 2;
        for (var y = 0; y < height; y++)
        {
            for (var x = half; x < width; x++)
            {
                image[x, y] = new L8(255);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Máscara L8 de UN cuadrante (2x2, `quadrant` 0=top-left, 1=top-right,
    /// 2=bottom-left, 3=bottom-right) en blanco (255), el resto en negro
    /// (0) -- 4 máscaras que NO se solapan y cubren toda la imagen, usadas
    /// para replicar en tests un fixture de 4 colores real (M2.1-S01, ver
    /// spec.md: "un fixture de cuatro colores debe devolver al menos 4
    /// grupos/capas coherentes").
    /// </summary>
    public static byte[] QuadrantMask(int width, int height, int quadrant)
    {
        using var image = new Image<L8>(width, height, new L8(0));
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        var (xStart, xEnd) = quadrant % 2 == 0 ? (0, halfWidth) : (halfWidth, width);
        var (yStart, yEnd) = quadrant < 2 ? (0, halfHeight) : (halfHeight, height);

        for (var y = yStart; y < yEnd; y++)
        {
            for (var x = xStart; x < xEnd; x++)
            {
                image[x, y] = new L8(255);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>PNG RGBA transparente de las dimensiones dadas -- suficiente para probar el flujo de guardado del preview.</summary>
    public static byte[] TransparentPreview(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 0));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
