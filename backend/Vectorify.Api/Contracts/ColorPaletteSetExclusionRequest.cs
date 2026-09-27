namespace Vectorify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../color-palette/{paletteId}/exclude (M2.1-S02):
/// marca un grupo como incluido/excluido, INDEPENDIENTE de un merge --
/// acción explícita del usuario, tanto para cambiar la sugerencia inicial de
/// fondo dominante (ver ColorPaletteService.DetectAsync) como para excluir
/// cualquier otro color a mano.
/// </summary>
public sealed record ColorPaletteSetExclusionRequest(Guid GroupId, bool IsExcluded);
