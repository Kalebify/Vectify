namespace Vectorify.Api.Assets;

/// <summary>
/// Construye la clave de storage NUEVA para Assets (M2.2-S04):
/// <c>projects/{projectId}/{type}/{assetId}{extensión}</c> -- deliberadamente distinta de la
/// convención usada por el flujo clásico de <see cref="Vectorify.Api.Storage.IFileStorage"/>
/// (p. ej. <c>{projectId}/{imageId}/original.ext</c>, ver registries bajo
/// <c>appsettings.json</c>): esta tarjeta NO migra las claves existentes, solo define una
/// convención nueva para el código nuevo (ver spec.md, "Hallazgo clave" y "Ambigüedades
/// detectadas"). <c>{type}</c> es <see cref="Vectorify.Api.Data.Asset.Type"/> ya normalizado
/// (minúsculas, charset validado) por <c>AssetService</c> antes de llegar acá.
///
/// Seguridad (ver spec.md): la clave NUNCA depende de <see cref="Vectorify.Api.Data.Asset.FileName"/>
/// (nombre crudo del usuario) -- se deriva siempre de <c>assetId</c>/<c>type</c>/extensión
/// derivada del MIME type ya validado. Un <c>FileName</c> con intento de path traversal
/// (p. ej. <c>"../../etc/passwd"</c>) nunca participa de esta clave.
/// </summary>
public static class AssetKeyFactory
{
    /// <summary>
    /// Tabla MIME -> extensión (documentada también en IMPL.md). Único mapeo usado para
    /// derivar la extensión de la clave de storage -- nunca la extensión del nombre de
    /// archivo que mandó el usuario.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExtensionsByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/webp"] = ".webp",
            ["image/svg+xml"] = ".svg",
        };

    /// <summary>True y la extensión (con punto, p. ej. ".png") si el content type tiene un mapeo conocido.</summary>
    public static bool TryGetExtension(string contentType, out string extension) =>
        ExtensionsByContentType.TryGetValue(contentType, out extension!);

    /// <summary>
    /// <c>projects/{projectId:N}/{type}/{assetId:N}{extension}</c>. <paramref name="type"/>
    /// se asume ya normalizado/validado por el llamador (ver <c>AssetService</c>) -- esta
    /// función no vuelve a sanitizarlo, solo compone la clave.
    /// </summary>
    public static string BuildKey(Guid projectId, string type, Guid assetId, string extension) =>
        $"projects/{projectId:N}/{type}/{assetId:N}{extension}";
}
