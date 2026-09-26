namespace Vectify.Api.Contracts;

/// <summary>
/// Un grupo lógico de componentes -- ver Vectify.Api.Components.ComponentGroup.
/// `IsStale`/`MissingComponentIds` se recalculan en cada respuesta contra la
/// ComponentSetVersion vigente del VectorId (nunca contra un snapshot viejo):
/// permiten a React avisar sin romper si algún componentId referenciado ya
/// no aparece en el análisis más reciente, sin que el backend tenga que
/// migrar ni invalidar el grupo automáticamente.
/// </summary>
public sealed record ComponentGroupPayload(
    Guid GroupId,
    string Name,
    IReadOnlyList<string> ComponentIds,
    bool IsStale,
    IReadOnlyList<string> MissingComponentIds);

/// <summary>
/// Respuesta de POST/GET .../vectors/{vectorId}/components/groups (M2-S05):
/// el conjunto de grupos lógicos vigente de UNA capa vectorial, siempre la
/// última versión creada por agrupar/desagrupar/renombrar.
/// </summary>
public sealed record ComponentGroupSetResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid VectorId,
    int Version,
    IReadOnlyList<ComponentGroupPayload> Groups);
