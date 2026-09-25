namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../color-palette/{paletteId}/merge: fusiona 2+
/// grupos (GroupId de la ÚLTIMA versión de esa paleta) en uno solo. `Name`
/// es opcional (si se omite, se sintetiza a partir de los nombres de los
/// grupos fusionados -- ver ColorPaletteService).
/// </summary>
public sealed record ColorPaletteMergeRequest(IReadOnlyList<Guid> GroupIds, string? Name);
