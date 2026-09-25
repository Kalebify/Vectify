namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../color-palette/{paletteId}/unmerge: deshace el
/// ÚLTIMO merge que produjo el grupo `GroupId` (mientras la paleta no esté
/// confirmada), restaurando los grupos que lo integraban.
/// </summary>
public sealed record ColorPaletteUnmergeRequest(Guid GroupId);
