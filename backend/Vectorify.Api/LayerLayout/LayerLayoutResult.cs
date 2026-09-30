using Vectorify.Api.VectorLayers;

namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Resultado de mutar el layout (order/visible/locked) de una capa/del
/// conjunto: SIEMPRE produce -- o falla en producir -- un
/// <see cref="LayerLayoutSetVersion"/> NUEVO, nunca muta uno existente. Sin
/// UpstreamError: es una edición de metadata pura, sin ninguna llamada a
/// Python/storage de por medio, ni tocar geometría -- mismo criterio que
/// Vectorify.Api.ManufacturingOperations.ManufacturingOperationResult.
/// </summary>
public abstract record LayerLayoutResult
{
    private LayerLayoutResult()
    {
    }

    /// <summary>Nueva versión del layout, lista, junto con el VectorLayerSetVersion vigente contra el que se validó/guardó.</summary>
    public sealed record Ready(LayerLayoutSetVersion Record, VectorLayerSetVersion LayerSet) : LayerLayoutResult;

    /// <summary>
    /// No existe un conjunto de capas (M2-S02) generado para esa paleta, o el
    /// groupId indicado no existe entre las capas de la versión vigente (el
    /// endpoint responde 404).
    /// </summary>
    public sealed record NotFound(string Code, string Message) : LayerLayoutResult;

    /// <summary>Los parámetros enviados son inválidos -- ej. Reorder con un conjunto de groupId que no coincide exactamente con las capas vigentes (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : LayerLayoutResult;
}
