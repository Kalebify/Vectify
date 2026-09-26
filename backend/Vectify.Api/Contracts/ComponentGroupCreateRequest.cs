namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../vectors/{vectorId}/components/groups: agrupa los
/// componentIds indicados (deben existir, al menos 2 distintos, en la
/// ComponentSetVersion vigente de ese VectorId) bajo un nuevo
/// <see cref="Vectify.Api.Components.ComponentGroup"/>. `Name` es opcional
/// (si se omite, se sintetiza un nombre por defecto -- ver
/// ComponentGroupService).
/// </summary>
public sealed record ComponentGroupCreateRequest(IReadOnlyList<string> ComponentIds, string? Name);
