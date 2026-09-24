using System.Buffers.Binary;

namespace Vectify.Api.Imaging;

/// <summary>
/// Lee width/height desde la cabecera de PNG/JPEG/WEBP sin decodificar la imagen
/// completa y sin agregar una dependencia de procesamiento de imágenes. Es
/// best-effort a propósito: spec.md pide width/height "cuando estén disponibles",
/// así que cualquier fallo de parseo simplemente devuelve false (el resto de la
/// carga sigue funcionando sin dimensiones).
/// </summary>
public static class ImageDimensionsReader
{
    public static bool TryRead(Stream stream, string contentType, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!stream.CanSeek)
        {
            return false;
        }

        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            return contentType switch
            {
                "image/png" => TryReadPng(stream, out width, out height),
                "image/jpeg" => TryReadJpeg(stream, out width, out height),
                "image/webp" => TryReadWebp(stream, out width, out height),
                _ => false,
            };
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            width = 0;
            height = 0;
            return false;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static bool TryReadPng(Stream stream, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Firma (8 bytes) + longitud de chunk IHDR (4) + "IHDR" (4) + width (4) + height (4) = 24.
        Span<byte> header = stackalloc byte[24];
        if (ReadFully(stream, header) < 24)
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(Stream stream, out int width, out int height)
    {
        width = 0;
        height = 0;

        Span<byte> soi = stackalloc byte[2];
        if (ReadFully(stream, soi) < 2 || soi[0] != 0xFF || soi[1] != 0xD8)
        {
            return false;
        }

        Span<byte> lengthBuffer = stackalloc byte[2];
        Span<byte> sof = stackalloc byte[5]; // precision(1) + height(2) + width(2)

        while (true)
        {
            var first = stream.ReadByte();
            if (first != 0xFF)
            {
                return false;
            }

            var marker = stream.ReadByte();
            while (marker == 0xFF)
            {
                marker = stream.ReadByte();
            }

            if (marker < 0 || marker == 0xD9) // EOI sin encontrar un SOF
            {
                return false;
            }

            // Marcadores sin payload asociado (RSTn, TEM).
            if (marker is (>= 0xD0 and <= 0xD7) or 0x01)
            {
                continue;
            }

            if (ReadFully(stream, lengthBuffer) < 2)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBuffer);
            if (segmentLength < 2)
            {
                return false;
            }

            // SOFn: 0xC0-0xCF salvo DHT (0xC4), JPG extendido (0xC8) y DAC (0xCC).
            var isStartOfFrame = marker is (>= 0xC0 and <= 0xCF) and not 0xC4 and not 0xC8 and not 0xCC;
            if (isStartOfFrame)
            {
                if (ReadFully(stream, sof) < 5)
                {
                    return false;
                }

                height = (sof[1] << 8) | sof[2];
                width = (sof[3] << 8) | sof[4];
                return width > 0 && height > 0;
            }

            stream.Seek(segmentLength - 2, SeekOrigin.Current);
        }
    }

    private static bool TryReadWebp(Stream stream, out int width, out int height)
    {
        width = 0;
        height = 0;

        Span<byte> riffHeader = stackalloc byte[12]; // "RIFF" + size(4) + "WEBP"
        if (ReadFully(stream, riffHeader) < 12)
        {
            return false;
        }

        Span<byte> chunkHeader = stackalloc byte[8]; // fourCC(4) + size(4)
        if (ReadFully(stream, chunkHeader) < 8)
        {
            return false;
        }

        var fourCc = System.Text.Encoding.ASCII.GetString(chunkHeader[..4]);

        if (fourCc == "VP8X")
        {
            Span<byte> payload = stackalloc byte[10]; // flags(1) + reserved(3) + width-1(3) + height-1(3)
            if (ReadFully(stream, payload) < 10)
            {
                return false;
            }

            width = 1 + (payload[4] | (payload[5] << 8) | (payload[6] << 16));
            height = 1 + (payload[7] | (payload[8] << 8) | (payload[9] << 16));
            return width > 0 && height > 0;
        }

        if (fourCc == "VP8 ")
        {
            Span<byte> payload = stackalloc byte[10];
            if (ReadFully(stream, payload) < 10)
            {
                return false;
            }

            if (payload[3] != 0x9D || payload[4] != 0x01 || payload[5] != 0x2A)
            {
                return false;
            }

            width = (payload[6] | (payload[7] << 8)) & 0x3FFF;
            height = (payload[8] | (payload[9] << 8)) & 0x3FFF;
            return width > 0 && height > 0;
        }

        if (fourCc == "VP8L")
        {
            Span<byte> payload = stackalloc byte[5]; // signature(1) + bitstream(4)
            if (ReadFully(stream, payload) < 5 || payload[0] != 0x2F)
            {
                return false;
            }

            var bits = (uint)(payload[1] | (payload[2] << 8) | (payload[3] << 16) | (payload[4] << 24));
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return width > 0 && height > 0;
        }

        return false;
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
