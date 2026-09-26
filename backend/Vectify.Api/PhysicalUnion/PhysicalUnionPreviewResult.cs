namespace Vectify.Api.PhysicalUnion;

/// <summary>
/// Resultado de pedir un PREVIEW de unión física: NUNCA persiste nada (ni
/// una VectorVersion nueva, ni un registro de auditoría) sin importar el
/// resultado -- confirmar es una operación EXPLÍCITA y separada (ver
/// <see cref="PhysicalUnionConfirmResult"/>). Cancelar (del lado de React)
/// no requiere ningún llamado adicional: simplemente no se pide `confirm`.
/// </summary>
public abstract record PhysicalUnionPreviewResult
{
    private PhysicalUnionPreviewResult()
    {
    }

    /// <summary>La unión SÍ fue geométricamente posible: preview listo para mostrar antes/después.</summary>
    public sealed record Ready(PhysicalUnionOutcome Outcome) : PhysicalUnionPreviewResult;

    /// <summary>No existe un análisis de componentes (M2-S03) o una VectorVersion con ese VectorId (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : PhysicalUnionPreviewResult;

    /// <summary>La selección es inválida: menos de 2 componentes distintos, o uno o más IDs no existen (el endpoint responde 400/422).</summary>
    public sealed record ValidationFailed(string Code, string Message) : PhysicalUnionPreviewResult;

    /// <summary>
    /// La unión NO fue geométricamente posible: geometría de entrada
    /// autointersectante/degenerada, o la validación post-operación no
    /// negociable ("nunca fingir unión") falló -- el mensaje explica por
    /// qué (el endpoint responde 422, ninguna versión previa se toca).
    /// </summary>
    public sealed record GeometryImpossible(string Code, string Message) : PhysicalUnionPreviewResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : PhysicalUnionPreviewResult;
}
