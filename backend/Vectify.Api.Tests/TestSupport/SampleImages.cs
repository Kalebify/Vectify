namespace Vectify.Api.Tests.TestSupport;

/// <summary>
/// Bytes mínimos válidos (o deliberadamente inválidos) de PNG/JPEG/WEBP para tests
/// de validación y de lectura de dimensiones, sin depender de archivos reales en
/// disco ni de una librería de imágenes.
/// </summary>
internal static class SampleImages
{
    /// <summary>PNG 1x1 real y válido (firma + IHDR con width=1, height=1).</summary>
    public static byte[] ValidPng1x1 => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    /// <summary>
    /// JPEG sintético (SOI + SOF0 con width=32, height=16). No es una imagen
    /// completa/decodificable, pero alcanza para validar firma y parseo de cabecera,
    /// que es todo lo que este sprint necesita.
    /// </summary>
    public static byte[] ValidJpegHeader32x16 =>
    [
        0xFF, 0xD8, // SOI
        0xFF, 0xC0, // SOF0
        0x00, 0x08, // longitud de segmento = 8
        0x08, // precisión
        0x00, 0x10, // height = 16
        0x00, 0x20, // width = 32
    ];

    /// <summary>WEBP extendido (VP8X) sintético con width=100, height=200.</summary>
    public static byte[] ValidWebpVp8x100x200 =>
    [
        (byte)'R', (byte)'I', (byte)'F', (byte)'F',
        0x16, 0x00, 0x00, 0x00, // tamaño RIFF (no se valida)
        (byte)'W', (byte)'E', (byte)'B', (byte)'P',
        (byte)'V', (byte)'P', (byte)'8', (byte)'X',
        0x0A, 0x00, 0x00, 0x00, // tamaño del chunk VP8X
        0x00, 0x00, 0x00, 0x00, // flags + reserved
        99, 0x00, 0x00, // width - 1 = 99 -> width = 100
        199, 0x00, 0x00, // height - 1 = 199 -> height = 200
    ];

    /// <summary>Bytes que no son ninguna firma válida (para casos "corrupto").</summary>
    public static byte[] NotAnImage => "esto no es una imagen"u8.ToArray();
}
