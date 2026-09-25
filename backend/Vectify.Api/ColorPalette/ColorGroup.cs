namespace Vectify.Api.ColorPalette;

/// <summary>
/// Un color/grupo dentro de una <see cref="ColorPaletteVersion"/>: color
/// representativo, área aproximada, y una máscara persistida (PNG 0/255,
/// mismas dimensiones que la imagen de origen) recuperable vía
/// <see cref="MaskStorageKey"/>. <see cref="RawGroupIds"/> son los índices
/// 0-based que Python asignó a los clusters detectados originalmente (ver
/// app.core.color_palette_pipeline) que integran este grupo -- estables
/// durante toda la sesión de detección, se usan para recomponer la máscara
/// combinada tras un merge sin volver a llamar a Python.
///
/// <see cref="MergedFrom"/> es la snapshot de los grupos EXACTOS (tal cual
/// estaban, con su propio MergedFrom anidado si correspondía) que se
/// fusionaron para crear este grupo -- null/vacío si es un grupo tal cual lo
/// detectó Python, nunca fusionado. Permite deshacer un merge (unmerge)
/// restaurando esos grupos sin tener que recalcular nada, y encadenar varios
/// niveles de unmerge si hubo merges sucesivos (ver
/// Vectify.Api.ColorPalette.ColorPaletteService.UnmergeAsync).
/// </summary>
public sealed record ColorGroup(
    Guid GroupId,
    string Name,
    string ColorHex,
    long PixelCount,
    double AreaPercent,
    bool HasPartialAlpha,
    IReadOnlyList<int> RawGroupIds,
    string MaskStorageKey,
    IReadOnlyList<ColorGroup>? MergedFrom);
