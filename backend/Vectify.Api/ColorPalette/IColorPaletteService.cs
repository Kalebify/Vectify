using Vectify.Api.Contracts;

namespace Vectify.Api.ColorPalette;

/// <summary>
/// Orquesta el flujo de paleta de colores (M2-S01): detectar (llama a
/// Python, cachea por sesión+parámetros, versiona), fusionar/deshacer
/// fusión/renombrar grupos (ediciones puras de metadata sobre la última
/// versión de una sesión, sin volver a llamar a Python) y confirmar. Cada
/// operación exitosa crea una <see cref="ColorPaletteVersion"/> NUEVA --
/// nunca muta una existente.
/// </summary>
public interface IColorPaletteService
{
    /// <summary>
    /// Detecta la paleta de colores de la imagen original YA subida. Si
    /// <c>request.PaletteId</c> es null, arranca una sesión nueva; si se
    /// informa, re-detecta bajo esa MISMA sesión (mismos parámetros ->
    /// cache-hit, crea versión nueva reutilizando los grupos ya detectados;
    /// parámetros distintos -> llama a Python de nuevo).
    /// </summary>
    Task<ColorPaletteResult> DetectAsync(
        Guid projectId, Guid imageId, ColorPaletteDetectRequest request, CancellationToken cancellationToken);

    /// <summary>Fusiona 2+ grupos de la última versión de la sesión en uno solo.</summary>
    Task<ColorPaletteResult> MergeAsync(
        Guid projectId, Guid imageId, Guid paletteId, ColorPaletteMergeRequest request, CancellationToken cancellationToken);

    /// <summary>Deshace el último merge que produjo el grupo indicado (mientras la sesión no esté confirmada).</summary>
    Task<ColorPaletteResult> UnmergeAsync(
        Guid projectId, Guid imageId, Guid paletteId, ColorPaletteUnmergeRequest request, CancellationToken cancellationToken);

    /// <summary>Renombra un grupo de la última versión de la sesión.</summary>
    Task<ColorPaletteResult> RenameAsync(
        Guid projectId, Guid imageId, Guid paletteId, ColorPaletteRenameRequest request, CancellationToken cancellationToken);

    /// <summary>Confirma la paleta: a partir de acá, la sesión ya no admite merge/unmerge/rename/re-detección.</summary>
    Task<ColorPaletteResult> ConfirmAsync(Guid projectId, Guid imageId, Guid paletteId, CancellationToken cancellationToken);

    /// <summary>Recupera la última versión de una sesión, o null si no existe.</summary>
    ColorPaletteVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId);
}
