namespace Vectorify.Api.Data;

/// <summary>
/// Un archivo binario referenciado por clave de storage (M2.2-S02). <see cref="StorageKey"/>
/// es solo una columna preparada para cuando M2.2-S04 (Object Storage + gestión de Assets)
/// implemente storage real de binarios -- esta tarjeta NO escribe/lee archivos reales acá
/// (ver "Fuera de alcance" de spec.md). <see cref="Type"/> es texto libre, no un enum
/// cerrado: esta tarjeta no tiene evidencia suficiente para fijar de antemano el conjunto
/// final de tipos de asset (imagen fuente subida, snapshot SVG, thumbnail, etc.) -- ver el
/// ADR en IMPL.md.
/// </summary>
public sealed class Asset
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Type { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long Size { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
