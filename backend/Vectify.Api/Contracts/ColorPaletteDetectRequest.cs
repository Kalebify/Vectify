namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../color-palette/detect. Si <see cref="PaletteId"/>
/// es null, arranca una sesión de paleta nueva (primera detección sobre esta
/// imagen); si se informa, debe referenciar una sesión YA existente y NO
/// confirmada para esta imagen -- re-detecta con los parámetros dados bajo
/// el MISMO PaletteId (descartando cualquier merge/rename previo: vuelve a
/// los grupos crudos que detecta Python), mismo criterio de "un ID de
/// recurso de origen en el body" que el resto del pipeline.
/// Tolerance/MaxColors son opcionales: si se omiten, se usan los defaults
/// configurados (Vectify.Api.Options.ColorPaletteOptions).
/// </summary>
public sealed record ColorPaletteDetectRequest(Guid? PaletteId, double? Tolerance, int? MaxColors);
