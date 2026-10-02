using Vectorify.Api.Data;

namespace Vectorify.Api.VectorDocuments.Persistence;

/// <summary>
/// Acceso a datos de <see cref="VectorDocument"/>/<see cref="DocumentVersion"/>/<see cref="Layer"/>/
/// <see cref="PaletteColor"/> (M2.2-S05) contra PostgreSQL vía EF Core. Mismo patrón
/// arquitectónico que <see cref="Vectorify.Api.Projects.Persistence.IProjectRepository"/>
/// (M2.2-S03)/<see cref="Vectorify.Api.Assets.Persistence.IAssetRepository"/> (M2.2-S04): vive
/// en un sub-namespace <c>.Persistence</c> propio, y NO resuelve ownership "de negocio" --
/// recibe <c>ownerId</c> explícito y SIEMPRE filtra por él, nunca una query sin ese filtro.
///
/// A diferencia de esos dos repositorios (CRUD simple sobre una sola tabla), este orquesta la
/// escritura de TODO el grafo (VectorDocument + DocumentVersion + Layer + PaletteColor + el
/// puntero Project.CurrentVersionId) en una ÚNICA transacción EF Core -- ver
/// <see cref="VectorDocumentRepository.SaveAsync"/> para el detalle de por qué.
/// </summary>
public interface IVectorDocumentRepository
{
    /// <summary>
    /// Agrega una <see cref="DocumentVersion"/> nueva (creando el <see cref="VectorDocument"/>
    /// si el <see cref="Project"/> todavía no tenía uno) con los Layers/PaletteColors del
    /// snapshot, y actualiza <see cref="Project.CurrentVersionId"/>/<see cref="Project.UpdatedAt"/>
    /// -- todo en un único <c>SaveChangesAsync</c>. Null si el proyecto no existe o no
    /// pertenece a <paramref name="ownerId"/>. Puede lanzar
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> si el
    /// concurrency token (xmin) de <see cref="Project"/> cambió desde que se leyó la fila
    /// (Save concurrente) -- <see cref="VectorDocumentService"/> la traduce a
    /// <see cref="VectorDocumentResult.Conflict"/> (409).
    /// </summary>
    Task<VectorDocumentSaveOutcome?> SaveAsync(
        Guid projectId, Guid ownerId, DocumentSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>
    /// La <see cref="VectorDocument"/>/<see cref="DocumentVersion"/> ACTUAL
    /// (<see cref="Project.CurrentVersionId"/>) de un proyecto, con Layers (+ su
    /// <see cref="PaletteColor"/>) y PaletteColors ya cargados. Null si el proyecto no existe,
    /// no pertenece a <paramref name="ownerId"/>, o no tiene ninguna versión guardada todavía
    /// (<see cref="Project.CurrentVersionId"/> null) -- mismo 404 uniforme para los tres casos.
    /// </summary>
    Task<(VectorDocument Document, DocumentVersion Version)?> FindCurrentDocumentAsync(
        Guid projectId, Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// Aplica <paramref name="patch"/> sobre el <see cref="Layer"/> <paramref name="layerId"/>,
    /// SOLO si pertenece a la <see cref="DocumentVersion"/> ACTUAL (<see cref="Project.CurrentVersionId"/>)
    /// del proyecto <paramref name="projectId"/> perteneciente a <paramref name="ownerId"/> --
    /// null en cualquier otro caso (no existe, pertenece a otro proyecto/usuario, o pertenece a
    /// una versión histórica ya superada), mismo 404 uniforme que el resto del módulo.
    /// </summary>
    Task<Layer?> UpdateLayerAsync(
        Guid projectId, Guid ownerId, Guid layerId, LayerPatch patch, CancellationToken cancellationToken);
}
