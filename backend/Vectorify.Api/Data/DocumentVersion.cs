namespace Vectorify.Api.Data;

/// <summary>
/// Una versión de un <see cref="VectorDocument"/> (M2.2-S02). <see cref="VersionNumber"/>
/// es único POR <see cref="VectorDocumentId"/> (constraint compuesto configurado en
/// <see cref="VectorizationDbContext.OnModelCreating"/>) -- nunca un índice único global:
/// dos documentos distintos SÍ pueden tener ambos una versión "1". <see cref="SvgAssetId"/>
/// referencia el snapshot SVG real como asset (nullable: una versión puede existir sin
/// snapshot generado todavía). <see cref="MetadataJson"/> se persiste como JSONB real
/// (Npgsql lo soporta nativamente) para metadata heterogénea/evolutiva que no amerita
/// columnas propias todavía (ej. parámetros de la última vectorización) -- ver ADR en
/// IMPL.md.
/// </summary>
public sealed class DocumentVersion
{
    public Guid Id { get; set; }

    public Guid VectorDocumentId { get; set; }

    public VectorDocument? VectorDocument { get; set; }

    public int VersionNumber { get; set; }

    public Guid? SvgAssetId { get; set; }

    public Asset? SvgAsset { get; set; }

    public string Origin { get; set; } = string.Empty;

    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Layer> Layers { get; set; } = new List<Layer>();

    public ICollection<PaletteColor> PaletteColors { get; set; } = new List<PaletteColor>();
}
