using System.Text;

namespace Vectify.Api.Export;

/// <summary>
/// Función pura que deriva un nombre de archivo seguro de descarga a partir
/// del nombre original subido por el usuario (<c>ProjectRecord.FileName</c>,
/// controlado libremente por el usuario en M1-S02: puede tener tildes,
/// espacios, símbolos, o caracteres inválidos para un sistema de archivos o
/// un header HTTP -- ver spec.md, caso de prueba explícito "nombres con
/// caracteres especiales"). NO se ocupa de la codificación ASCII/UTF-8 del
/// header <c>Content-Disposition</c> -- eso lo resuelve
/// <see cref="Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.SetHttpFileName"/>
/// en <c>Endpoints.ExportEndpoints</c> (RFC 6266, dos partes: <c>filename=</c>
/// ASCII de respaldo + <c>filename*=UTF-8''...</c> con el nombre real) -- acá
/// solo se remueven los caracteres que romperían un nombre de archivo en
/// CUALQUIER sistema operativo (separadores de ruta, caracteres reservados de
/// Windows, caracteres de control), se preservan tildes/espacios/símbolos
/// benignos, y se compone el nombre final con el sufijo de la etapa +
/// ".svg".
/// </summary>
public static class ExportFileNameSanitizer
{
    /// <summary>Nombre base cuando el original queda vacío tras sanitizar (ej. solo emojis/símbolos de control) o no hay ProjectRecord disponible.</summary>
    public const string FallbackBaseName = "export";

    // Longitud generosa pero acotada: evita nombres/headers absurdamente
    // largos si el usuario subió un archivo con un nombre kilométrico, sin
    // arriesgarse a truncar en medio de un carácter compuesto (se trunca por
    // unidades UTF-16 de .NET, suficiente para este propósito).
    private const int MaxBaseNameLength = 80;

    // Caracteres reservados por NTFS/Windows en nombres de archivo (más
    // restrictivo que ext4/APFS, así que cubre los tres sistemas operativos
    // de destino de este proyecto). No se usa Path.GetInvalidFileNameChars()
    // porque su resultado depende del sistema operativo donde CORRE la Web
    // API, y el nombre generado tiene que ser válido en la máquina del
    // USUARIO que lo descarga, sin importar dónde corra el backend.
    private static readonly char[] ReservedChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Compone el nombre final de descarga: <c>{baseSanitizado}-{stageSuffix}.svg</c>.
    /// </summary>
    public static string Build(string? originalFileName, string stageSuffix)
    {
        var baseName = SanitizeBaseName(originalFileName);
        var suffix = string.IsNullOrWhiteSpace(stageSuffix) ? "export" : stageSuffix.Trim();
        return $"{baseName}-{suffix}.svg";
    }

    /// <summary>
    /// Sanitiza el nombre original SIN la extensión ni el sufijo de etapa --
    /// expuesto por separado para poder testearlo de forma aislada.
    /// </summary>
    public static string SanitizeBaseName(string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return FallbackBaseName;
        }

        // Defensivo: quita cualquier componente de directorio. ProjectUploadService
        // ya aplica Path.GetFileName al guardar (Projects/ProjectUploadService.cs),
        // pero este método es una función pura que no debe confiar ciegamente en
        // el dato que recibe.
        var lastSeparator = originalFileName.LastIndexOfAny(['/', '\\']);
        var nameOnly = lastSeparator >= 0 ? originalFileName[(lastSeparator + 1)..] : originalFileName;

        // Quita la extensión original: el archivo exportado siempre termina en
        // ".svg" sin importar el formato del original subido (png/jpg/etc.).
        var dotIndex = nameOnly.LastIndexOf('.');
        var withoutExtension = dotIndex > 0 ? nameOnly[..dotIndex] : nameOnly;

        var builder = new StringBuilder(withoutExtension.Length);
        foreach (var ch in withoutExtension)
        {
            if (char.IsControl(ch) || Array.IndexOf(ReservedChars, ch) >= 0)
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(ch);
            }
        }

        // Windows no permite terminar un nombre de archivo en espacio o punto.
        var sanitized = builder.ToString().Trim().Trim('.', ' ');

        if (sanitized.Length > MaxBaseNameLength)
        {
            sanitized = sanitized[..MaxBaseNameLength].TrimEnd('.', ' ', '_');
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return FallbackBaseName;
        }

        if (ReservedWindowsNames.Contains(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }
}
