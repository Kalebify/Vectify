namespace Vectify.Api.Validation;

/// <summary>
/// Verifica los primeros bytes ("magic numbers") de un archivo contra la firma
/// esperada de su MIME type declarado. Un archivo corrupto o truncado, o uno cuyo
/// contenido no coincide con la extensión/Content-Type declarados, falla acá aunque
/// haya pasado la validación de extensión/MIME.
/// </summary>
internal static class ImageSignature
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// Lee hasta 12 bytes desde el inicio del stream para comparar la firma y
    /// deja el stream reposicionado en 0 para que se pueda volver a leer completo.
    /// </summary>
    public static bool Matches(Stream stream, string contentType)
    {
        if (!stream.CanSeek)
        {
            return true; // no se puede inspeccionar sin seek: no bloquear, la firma es un chequeo best-effort.
        }

        stream.Position = 0;
        Span<byte> buffer = stackalloc byte[12];
        var read = ReadFully(stream, buffer);
        stream.Position = 0;

        return contentType switch
        {
            "image/png" => read >= PngMagic.Length && buffer[..PngMagic.Length].SequenceEqual(PngMagic),
            "image/jpeg" => read >= JpegMagic.Length && buffer[..JpegMagic.Length].SequenceEqual(JpegMagic),
            "image/webp" => read >= 12
                && buffer[0] == (byte)'R' && buffer[1] == (byte)'I' && buffer[2] == (byte)'F' && buffer[3] == (byte)'F'
                && buffer[8] == (byte)'W' && buffer[9] == (byte)'E' && buffer[10] == (byte)'B' && buffer[11] == (byte)'P',
            _ => false,
        };
    }

    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
