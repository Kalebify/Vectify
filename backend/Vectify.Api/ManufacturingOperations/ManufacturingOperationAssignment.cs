namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// La intención de fabricación asignada a UNA capa (identificada por su
/// <see cref="GroupId"/>, el mismo <see cref="Vectify.Api.VectorLayers.VectorLayer.GroupId"/>
/// heredado del <see cref="Vectify.Api.ColorPalette.ColorGroup"/> de origen --
/// ver spec.md, regla: "el color original identifica la capa, pero la
/// operación es independiente del color"). Nunca referencia geometría ni un
/// VectorId: es metadata pura.
/// </summary>
public sealed record ManufacturingOperationAssignment(
    Guid GroupId,
    ManufacturingOperationKind Operation,
    DateTimeOffset AssignedAt);
