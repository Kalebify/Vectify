namespace Vectorify.Api.Assets;

/// <summary>
/// Capa de aplicación entre <c>AssetEndpoints</c> e
/// <see cref="Persistence.IAssetRepository"/>/<see cref="Vectorify.Api.Storage.IFileStorage"/>
/// (M2.2-S04): valida el archivo/tipo, resuelve ownership (vía
/// <see cref="Vectorify.Api.Users.IUserContext"/> + <see cref="Vectorify.Api.Projects.Persistence.IProjectRepository"/>)
/// y orquesta la política de consistencia storage-primero/fila-después (ver spec.md,
/// "Ambigüedades detectadas"). Ningún endpoint llama a <c>IAssetRepository</c>/
/// <c>IFileStorage</c> directamente.
/// </summary>
public interface IAssetService
{
    /// <summary>
    /// Sube un Asset nuevo. <paramref name="type"/> es <see cref="Vectorify.Api.Data.Asset.Type"/>
    /// (p. ej. "original"/"preview"/"vector"/"export") -- participa de la clave de storage,
    /// así que se valida/normaliza acá (ver <see cref="AssetKeyFactory"/>).
    /// </summary>
    Task<AssetResult> UploadAsync(Guid projectId, string? type, IFormFile? file, CancellationToken cancellationToken);

    /// <summary>
    /// Sube un Asset nuevo a partir de bytes generados SERVER-SIDE (M2.2-S05) -- nunca expuesto
    /// como endpoint público, solo uso interno (p. ej. <c>VectorDocumentService</c> subiendo el
    /// SVG ya coloreado de una capa al Guardar). Reusa exactamente la misma
    /// <see cref="AssetKeyFactory"/>/política storage-primero-fila-después que
    /// <see cref="UploadAsync"/> -- la única diferencia es que no pasa por
    /// <see cref="IAssetUploadValidator"/> (pensado para <see cref="IFormFile"/> de un upload
    /// HTTP real) ni resuelve ownership por sí mismo: el llamador ya validó ownership del
    /// <paramref name="projectId"/> antes de llegar acá.
    /// </summary>
    Task<AssetResult> CreateFromBytesAsync(
        Guid projectId, string type, string fileName, string contentType, byte[] content, CancellationToken cancellationToken);

    Task<AssetResult> DownloadAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken);

    /// <summary>
    /// Hard delete intencional de un Asset individual: borra el archivo real Y la fila.
    /// Distinto del soft-delete de <see cref="Vectorify.Api.Data.Project"/> (M2.2-S03), que
    /// NUNCA toca Assets.
    /// </summary>
    Task<AssetResult> DeleteAsync(Guid projectId, Guid assetId, CancellationToken cancellationToken);
}
