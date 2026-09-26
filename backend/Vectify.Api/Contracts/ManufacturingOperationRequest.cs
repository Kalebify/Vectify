namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../color-palette/{paletteId}/layers/{groupId}/operation:
/// asigna (o reasigna) la intención de fabricación de una capa. `Operation`
/// es un string case-insensitive validado contra los 3 únicos valores
/// aceptados ("cut" | "engrave" | "ignore") -- ver
/// Vectify.Api.ManufacturingOperations.ManufacturingOperationParser. Nunca
/// "unassigned": ese es un estado derivado (ausencia de asignación), no algo
/// que se pueda enviar explícitamente.
/// </summary>
public sealed record ManufacturingOperationRequest(string Operation);
