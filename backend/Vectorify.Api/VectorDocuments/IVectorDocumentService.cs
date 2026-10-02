using Vectorify.Api.Contracts;

namespace Vectorify.Api.VectorDocuments;

/// <summary>
/// Capa de aplicación entre <c>VectorDocumentEndpoints</c> y
/// <see cref="Persistence.IVectorDocumentRepository"/> (M2.2-S05): resuelve ownership (vía
/// <see cref="Vectorify.Api.Users.IUserContext"/>), orquesta la lectura del estado clásico
/// VIGENTE (nunca confía en geometría/metadata del cliente), sube cada SVG de capa como
/// <see cref="Vectorify.Api.Data.Asset"/> y delega la escritura transaccional del grafo
/// completo al repositorio. Ningún endpoint llama a
/// <see cref="Persistence.IVectorDocumentRepository"/> directamente.
/// </summary>
public interface IVectorDocumentService
{
    Task<VectorDocumentResult> SaveAsync(VectorDocumentSaveRequest request, CancellationToken cancellationToken);

    Task<VectorDocumentResult> GetDocumentAsync(Guid projectId, CancellationToken cancellationToken);

    Task<VectorDocumentResult> UpdateLayerAsync(
        Guid projectId, Guid layerId, UpdateLayerRequest request, CancellationToken cancellationToken);
}
