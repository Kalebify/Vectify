namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// Los TRES únicos valores que puede tener la intención de fabricación de una
/// capa de color (M2-S02, <see cref="Vectify.Api.VectorLayers.VectorLayer.GroupId"/>):
/// M2-S07 es pura metadata semántica, NUNCA toca geometría ni dispara ningún
/// cálculo (ni .NET ni Python). Deliberadamente NO incluye un cuarto valor
/// "sin asignar": la ausencia de una <see cref="ManufacturingOperationAssignment"/>
/// para un groupId dado ES el estado "sin asignar" (ver spec.md, "Ambigüedades
/// detectadas" -- nunca asumir "Corte" por defecto silenciosamente). Ese
/// estado se representa en la capa de presentación (ver
/// Vectify.Api.Contracts.ManufacturingOperationPayload.Operation, wire value
/// "unassigned") y NUNCA es un valor que el cliente pueda enviar
/// explícitamente -- ver <see cref="ManufacturingOperationParser"/>.
/// </summary>
public enum ManufacturingOperationKind
{
    Cut,
    Engrave,
    Ignore,
}
