namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Metadata de UI persistida para UNA capa (identificada por su
/// <see cref="GroupId"/>, el mismo <see cref="Vectorify.Api.VectorLayers.VectorLayer.GroupId"/>
/// heredado del <see cref="Vectorify.Api.ColorPalette.ColorGroup"/> de origen):
/// posición visual (<see cref="Order"/>), visibilidad (<see cref="Visible"/>,
/// el Eye del panel Layers) y bloqueo de edición (<see cref="Locked"/>) --
/// M2.1-S07, lo que M2.1-S03 dejó explícitamente pendiente para "una tarjeta
/// posterior". Nunca referencia geometría ni un VectorId: es metadata pura de
/// navegación/edición del Workspace, mismo criterio que
/// <see cref="Vectorify.Api.ManufacturingOperations.ManufacturingOperationAssignment"/>
/// (metadata pura de fabricación) -- ambos son sidecars propios por
/// paleta+versión confirmada, deliberadamente NO agregados a
/// <see cref="Vectorify.Api.ColorPalette.ColorGroup"/>/
/// <see cref="Vectorify.Api.ColorPalette.ColorPaletteVersion"/>, que ya
/// tienen su propia responsabilidad bien acotada (detección/reducción de
/// color, no navegación del Workspace).
/// </summary>
public sealed record LayerLayoutEntry(Guid GroupId, int Order, bool Visible, bool Locked);
