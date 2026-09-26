namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../vectors/{vectorId}/physical-union/preview y
/// .../confirm: la selección de 2+ componentIds (de M2-S03, de la
/// ComponentSetVersion vigente de ESE VectorId) a fusionar físicamente.
/// Mismo shape que <see cref="ComponentGroupCreateRequest"/> salvo por
/// `Name` (la unión física no tiene nombre editable) -- deliberadamente
/// DISTINTO tipo (no reutiliza ComponentGroupCreateRequest): "Agrupar" y
/// "Unir físicamente" son acciones DISTINTAS con consecuencias distintas
/// (ver spec.md M2-S06, criterio de aceptación), sus contratos HTTP no
/// deberían acoplarse solo porque hoy tienen la misma forma.
/// </summary>
public sealed record PhysicalUnionRequest(IReadOnlyList<string> ComponentIds);
