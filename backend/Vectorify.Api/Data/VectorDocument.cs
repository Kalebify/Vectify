namespace Vectorify.Api.Data;

/// <summary>
/// El documento vectorial lógico de un <see cref="Project"/> (M2.2-S02): dimensiones
/// físicas (<see cref="WidthMm"/>/<see cref="HeightMm"/>), <see cref="ViewBox"/> SVG y
/// <see cref="SchemaVersion"/> del formato interno. La geometría real (los
/// <c>&lt;path d="..."&gt;</c>) NUNCA vive acá como filas -- cada
/// <see cref="DocumentVersion"/> la referencia como snapshot/asset vía
/// <see cref="DocumentVersion.SvgAssetId"/>, nunca como tablas de nodos/puntos. Ver el ADR
/// en IMPL.md para el criterio completo de normalización vs asset.
/// </summary>
public sealed class VectorDocument
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public double WidthMm { get; set; }

    public double HeightMm { get; set; }

    /// <summary>
    /// String plano (ej. "0 0 800 600"), no JSONB: se consulta poco y no amerita un tipo
    /// estructurado -- ver ADR en IMPL.md.
    /// </summary>
    public string ViewBox { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
}
