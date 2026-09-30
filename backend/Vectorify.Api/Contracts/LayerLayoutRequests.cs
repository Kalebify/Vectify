namespace Vectorify.Api.Contracts;

/// <summary>Cuerpo JSON de POST .../layers/{groupId}/visibility: persiste el Eye (hide/show) de una capa.</summary>
public sealed record SetLayerVisibleRequest(bool Visible);

/// <summary>Cuerpo JSON de POST .../layers/{groupId}/lock: persiste el Lock (bloqueo de edición) de una capa. NUNCA afecta Visible.</summary>
public sealed record SetLayerLockedRequest(bool Locked);

/// <summary>
/// Cuerpo JSON de POST .../layers/reorder: el nuevo orden visual COMPLETO
/// (Drag &amp; Drop) -- debe listar, exactamente una vez cada uno, todos los
/// groupId del conjunto de capas vigente de esa paleta.
/// </summary>
public sealed record ReorderLayersRequest(IReadOnlyList<Guid> OrderedGroupIds);
