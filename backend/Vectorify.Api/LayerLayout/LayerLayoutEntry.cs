namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Metadata de UI persistida para UNA capa (identificada por su
/// <see cref="GroupId"/>, el mismo <see cref="Vectorify.Api.VectorLayers.VectorLayer.GroupId"/>
/// heredado del <see cref="Vectorify.Api.ColorPalette.ColorGroup"/> de origen):
/// posición visual (<see cref="Order"/>), visibilidad (<see cref="Visible"/>,
/// el Eye del panel Layers), bloqueo de edición (<see cref="Locked"/>) y
/// nombre editable (<see cref="Name"/>) -- M2.1-S07, lo que M2.1-S03 dejó
/// explícitamente pendiente para "una tarjeta posterior" (Name se sumó en la
/// ronda de fix 2, ver IMPL-fix-round-1.md: <see cref="Vectorify.Api.ColorPalette.ColorPaletteService.RenameAsync"/>
/// rechaza con 409 "palette_confirmed" en cuanto la paleta está confirmada,
/// que es SIEMPRE el caso en el Workspace -- un nombre editable
/// post-confirmación es, igual que Order/Visible/Locked, metadata pura del
/// Workspace, no un cambio a la detección de color). <see cref="Name"/> es
/// `null` cuando esa capa nunca recibió un rename explícito -- en ese caso el
/// consumidor debe usar el nombre original de la capa
/// (<see cref="Vectorify.Api.VectorLayers.VectorLayer.Name"/>), nunca
/// inventar un valor acá. Nunca referencia geometría ni un VectorId: es
/// metadata pura de navegación/edición del Workspace, mismo criterio que
/// <see cref="Vectorify.Api.ManufacturingOperations.ManufacturingOperationAssignment"/>
/// (metadata pura de fabricación) -- ambos son sidecars propios por
/// paleta+versión confirmada, deliberadamente NO agregados a
/// <see cref="Vectorify.Api.ColorPalette.ColorGroup"/>/
/// <see cref="Vectorify.Api.ColorPalette.ColorPaletteVersion"/>, que ya
/// tienen su propia responsabilidad bien acotada (detección/reducción de
/// color, no navegación del Workspace).
/// </summary>
public sealed record LayerLayoutEntry(Guid GroupId, int Order, bool Visible, bool Locked, string? Name);
