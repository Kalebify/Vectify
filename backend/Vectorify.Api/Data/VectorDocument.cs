namespace Vectorify.Api.Data;

/// <summary>
/// El documento vectorial lógico de un <see cref="Project"/> (M2.2-S02): identidad/agrupación
/// PURA de su historial de <see cref="DocumentVersion"/>, sin estado propio (M2.2-S06). La
/// geometría real (los <c>&lt;path d="..."&gt;</c>) NUNCA vive acá como filas -- cada
/// <see cref="DocumentVersion"/> la referencia como snapshot/asset vía
/// <see cref="DocumentVersion.SvgAssetId"/>, nunca como tablas de nodos/puntos. Ver el ADR
/// en IMPL.md para el criterio completo de normalización vs asset.
///
/// <see cref="Data.DocumentVersion.WidthMm"/>/<see cref="Data.DocumentVersion.HeightMm"/>/
/// <see cref="Data.DocumentVersion.ViewBox"/>/<see cref="Data.DocumentVersion.SchemaVersion"/>
/// (M2.2-S06, migración <c>MoveDocumentDimensionsToVersion</c>): vivían acá sin versionar
/// hasta M2.2-S05 -- movidos a <see cref="DocumentVersion"/> porque restaurar una versión
/// vieja necesita poder devolver las dimensiones que esa versión tenía, aunque hayan
/// cambiado en saves posteriores (ver spec.md M2.2-S06, conflicto #2).
/// </summary>
public sealed class VectorDocument
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
}
