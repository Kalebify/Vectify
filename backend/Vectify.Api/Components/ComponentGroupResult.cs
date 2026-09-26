namespace Vectify.Api.Components;

/// <summary>
/// Resultado compartido por las operaciones de <see cref="IComponentGroupService"/>
/// (agrupar, desagrupar, renombrar): todas producen -- o fallan en producir
/// -- un <see cref="ComponentGroupSetVersion"/> NUEVO, nunca mutan uno
/// existente. A diferencia de ComponentSetResult, no hay UpstreamError: estas
/// operaciones son ediciones de metadata puras, sin ninguna llamada a
/// Python/storage de por medio.
/// </summary>
public abstract record ComponentGroupResult
{
    private ComponentGroupResult()
    {
    }

    /// <summary>Nueva versión del conjunto de grupos, lista.</summary>
    public sealed record Ready(ComponentGroupSetVersion Record) : ComponentGroupResult;

    /// <summary>
    /// No existe un análisis de componentes (M2-S03) para ese VectorId, o el
    /// GroupId indicado no existe en la última versión del conjunto (el
    /// endpoint responde 404).
    /// </summary>
    public sealed record NotFound(string Code, string Message) : ComponentGroupResult;

    /// <summary>
    /// La selección es inválida: menos de 2 componentes distintos, o uno o
    /// más IDs no existen en la ComponentSetVersion vigente para ese VectorId
    /// (el endpoint responde 400/422).
    /// </summary>
    public sealed record ValidationFailed(string Code, string Message) : ComponentGroupResult;
}
