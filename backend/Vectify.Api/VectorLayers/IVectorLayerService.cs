namespace Vectify.Api.VectorLayers;

/// <summary>
/// Orquesta la generación del conjunto completo de capas vectoriales de una
/// paleta de colores CONFIRMADA (M2-S02): una capa por cada
/// <see cref="Vectify.Api.ColorPalette.ColorGroup"/>, vectorizada de forma
/// independiente reutilizando el motor de M1-S05, en una única llamada al
/// motor Python. Cada operación exitosa crea una
/// <see cref="VectorLayerSetVersion"/> NUEVA -- nunca muta una existente.
/// </summary>
public interface IVectorLayerService
{
    /// <summary>
    /// Genera (o reutiliza desde caché, si ya se generó para exactamente esta
    /// paleta+versión confirmada) el conjunto completo de capas. Precondición:
    /// la <see cref="Vectify.Api.ColorPalette.ColorPaletteVersion"/> referenciada
    /// debe existir y estar confirmada -- si no, devuelve un error explícito
    /// sin llamar a Python.
    /// </summary>
    Task<VectorLayerSetResult> GenerateLayersAsync(
        Guid projectId, Guid imageId, Guid paletteId, CancellationToken cancellationToken);

    /// <summary>Recupera la última versión del conjunto de capas de una sesión de paleta, o null si nunca se generó una.</summary>
    VectorLayerSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId);
}
