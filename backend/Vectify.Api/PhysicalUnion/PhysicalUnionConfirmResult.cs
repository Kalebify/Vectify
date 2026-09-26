using Vectify.Api.Vectorization;

namespace Vectify.Api.PhysicalUnion;

/// <summary>
/// Resultado de CONFIRMAR una unión física: solo en <see cref="Ready"/> se
/// persistió algo -- una <see cref="VectorVersion"/> NUEVA (reutilizando el
/// tipo existente, ver spec.md) con el SVG fusionado, más un
/// <see cref="PhysicalUnionVersion"/> de auditoría. En CUALQUIER otro caso
/// (validación, geometría imposible, error upstream) NO se persiste nada:
/// la VectorVersion previa de la capa queda exactamente como estaba.
/// </summary>
public abstract record PhysicalUnionConfirmResult
{
    private PhysicalUnionConfirmResult()
    {
    }

    /// <summary>Unión confirmada y persistida: nueva VectorVersion + registro de auditoría.</summary>
    public sealed record Ready(VectorVersion NewVector, PhysicalUnionVersion Record) : PhysicalUnionConfirmResult;

    /// <summary>No existe un análisis de componentes (M2-S03) o una VectorVersion con ese VectorId (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : PhysicalUnionConfirmResult;

    /// <summary>La selección es inválida: menos de 2 componentes distintos, o uno o más IDs no existen (el endpoint responde 400/422).</summary>
    public sealed record ValidationFailed(string Code, string Message) : PhysicalUnionConfirmResult;

    /// <summary>La unión NO fue geométricamente posible -- nada se persiste, la versión previa queda intacta (el endpoint responde 422).</summary>
    public sealed record GeometryImpossible(string Code, string Message) : PhysicalUnionConfirmResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada -- nada se persiste (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : PhysicalUnionConfirmResult;
}
