namespace Vectorify.Api.Contracts;

/// <summary>
/// Una capa vista a través de su layout persistido (M2.1-S07): hereda
/// GroupId como único identificador estable -- nunca ColorHex/Fill (ver
/// spec.md, regla: "color no debe usarse como identificador primario").
/// </summary>
public sealed record LayerLayoutEntryPayload(Guid GroupId, int Order, bool Visible, bool Locked);

/// <summary>
/// Respuesta de POST .../layers/{groupId}/visibility, POST
/// .../layers/{groupId}/lock, POST .../layers/reorder y GET .../layers/layout:
/// siempre el layout COMPLETO vigente (una entrada por cada capa del conjunto
/// ACTUAL, incluidas las que nunca se tocaron -- con sus valores DEFAULT, ver
/// Vectorify.Api.LayerLayout.LayerLayoutDefaults). `Version` es 0 si esa
/// paleta+versión confirmada nunca recibió ninguna mutación de layout.
/// </summary>
public sealed record LayerLayoutSetResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid PaletteId,
    int PaletteVersion,
    Guid LayerSetId,
    int Version,
    IReadOnlyList<LayerLayoutEntryPayload> Entries);
