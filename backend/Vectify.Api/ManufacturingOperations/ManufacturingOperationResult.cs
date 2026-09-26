using Vectify.Api.VectorLayers;

namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// Resultado de <see cref="IManufacturingOperationService.AssignAsync"/>:
/// asignar SIEMPRE produce -- o falla en producir -- un
/// <see cref="ManufacturingOperationSetVersion"/> NUEVO, nunca muta uno
/// existente. Sin UpstreamError: es una edición de metadata pura, sin ninguna
/// llamada a Python/storage de por medio (ver
/// Vectify.Api.Components.ComponentGroupResult, mismo criterio).
/// </summary>
public abstract record ManufacturingOperationResult
{
    private ManufacturingOperationResult()
    {
    }

    /// <summary>Nueva versión del conjunto de asignaciones, lista, junto con el VectorLayerSetVersion vigente contra el que se validó/guardó.</summary>
    public sealed record Ready(ManufacturingOperationSetVersion Record, VectorLayerSetVersion LayerSet) : ManufacturingOperationResult;

    /// <summary>
    /// No existe un conjunto de capas (M2-S02) generado para esa paleta, o el
    /// groupId indicado no existe entre las capas de la versión vigente (el
    /// endpoint responde 404).
    /// </summary>
    public sealed record NotFound(string Code, string Message) : ManufacturingOperationResult;

    /// <summary>El valor de operación enviado no es ninguno de los 3 valores aceptados (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : ManufacturingOperationResult;
}
