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
    /// JPEG real y completamente decodificable (32x16), generado con ImageSharp.
    /// Antes era solo SOI+SOF0 sintético (no decodificable); ahora que
    /// ImageUploadValidator decodifica de verdad (ver Defecto 1), tiene que ser una
    /// imagen válida de punta a punta para los casos de éxito.
    /// </summary>
    public static byte[] ValidJpegHeader32x16 => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/wAARCAAQACADASIAAhEBAxEB/8QBogAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoLEAACAQMDAgQDBQUEBAAAAX0BAgMABBEFEiExQQYTUWEHInEUMoGRoQgjQrHBFVLR8CQzYnKCCQoWFxgZGiUmJygpKjQ1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4eLj5OXm5+jp6vHy8/T19vf4+foBAAMBAQEBAQEBAQEAAAAAAAABAgMEBQYHCAkKCxEAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9sAhAAIBgYHBgUIBwcHCQkICgwUDQwLCwwZEhMPFB0aHx4dGhwcICQuJyAiLCMcHCg3KSwwMTQ0NB8nOT04MjwuMzQyAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/2gAMAwEAAhEDEQA/APG0tvap0tvatBLb2qwlt7V9VKucVHEGclt7VYS29q0Etvap0tvasJVz1aOIP//Z");

    /// <summary>
    /// WEBP real y completamente decodificable (100x200), generado con ImageSharp.
    /// Antes era solo un chunk VP8X sintético (no decodificable); ver comentario de
    /// <see cref="ValidJpegHeader32x16"/>.
    /// </summary>
    public static byte[] ValidWebpVp8x100x200 => Convert.FromBase64String(
        "UklGRowBAABXRUJQVlA4IIABAAAQDgCdASpkAMgAPpFIn0ylpCKiIGgAsBIJZW7gBS91ljv9AK/+1QvbKM8Auv8vsAPtKORTSXW3/6f/z4//lv/9V1f////8Ff/////cJ//////7m/B////tZn9O34rf/3/3B4Aj3//l2f/////+PH//////nr6vs/RAAP73iaJVv3kU9mqhT/vDFX9s8PKjm3Yc4bP3DOG/JFnIFWt2vbrjwTDnfA+dHi2FIikenSqx3RC6xgKhRBFl4+W82CwVXtQz/Qjm0A5tZjTd6dSd+rIaYrwYX9+YnwiRLI4yqrtRMnZSJAlJqZLdZkBHFX76+XK+g6ruL5zPFkJVxfLwujy7CEYziTuGWJthXnYz5wTniJCMC5eYqNNJnjwoFTXIsm62u96edC89BKuwFCWEg3iDMmlOmGM792GzIA6Iq+6GSSZiLmXWSitO7Ejw5nE0J8MgSmck9/OwEI1kBBf3DelvkQCSXcNNcd35Vg6zozkdacRgEFIRrhCoqAa8tEgAAAA=");

    /// <summary>Bytes que no son ninguna firma válida (para casos "corrupto").</summary>
    public static byte[] NotAnImage => "esto no es una imagen"u8.ToArray();

    /// <summary>
    /// PNG con firma + cabecera IHDR válidas pero cortado a la mitad de su contenido
    /// real (no solo los 8 bytes de la firma): ImageSignature.Matches lo deja pasar,
    /// pero la decodificación real con ImageSharp debe fallar (Defecto 1).
    /// </summary>
    public static byte[] TruncatedPng => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACgAAAAoCAYAAACM/rhtAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAATUlEQVR4nO3OsQnAMBAEwTeo/7o=");

    /// <summary>
    /// JPEG con firma SOI/APP0 válida pero cortado a la mitad de su contenido real
    /// (bastante más allá de los primeros 8-12 bytes): debe rechazarse como corrupto
    /// una vez que se intenta decodificar de verdad.
    /// </summary>
    public static byte[] TruncatedJpeg => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/wAARCABAAEADASIAAhEBAxEB/8QBogAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoLEAACAQMDAgQDBQUEBAAAAX0BAgMABBEFEiExQQYTUWEHInEUMoGRoQgjQrHBFVLR8CQzYnKCCQoWFxgZGiUmJygpKjQ1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4eLj5OXm5+jp6vHy8/T19vf4+foBAAMBAQEBAQEBAQEAAAAAAAABAgMEBQYHCAkKCxEAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9sAhAAIBgYHBgUIBwcHCQkICgwUDQwLCwwZEhMPFB0aHx4dGhwcICQuJyAiLCMcHCg3KSwwMTQ0NB8nOT04MjwuMzQyAQkJCQwLDBgNDRgyIRw=");

    /// <summary>
    /// WEBP con firma RIFF/WEBP válida (más de 12 bytes) pero cortado antes de los
    /// datos de píxeles: debe rechazarse como corrupto al decodificar de verdad.
    /// </summary>
    public static byte[] TruncatedWebp => Convert.FromBase64String(
        "UklGRoYAAABXRUJQVlA4IHoAAACwBACd");
}
