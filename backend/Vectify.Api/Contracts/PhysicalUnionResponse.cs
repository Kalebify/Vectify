namespace Vectify.Api.Contracts;

/// <summary>
/// Respuesta de POST .../vectors/{vectorId}/physical-union/preview: geometría
/// REAL ya calculada (no una aproximación visual) del resultado de unir los
/// componentes indicados, SIN persistir nada -- el usuario ve `Svg` embebido
/// para renderizar el preview antes/después y `ComponentCountAfter` (debería
/// ser 1 si la unión fue exitosa) antes de decidir confirmar o cancelar.
/// </summary>
public sealed record PhysicalUnionPreviewResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid VectorId,
    IReadOnlyList<string> ComponentIds,
    string Svg,
    string ContentType,
    int Width,
    int Height,
    VectorMetricsPayload Metrics,
    int ComponentCountBefore,
    int ComponentCountAfter,
    string Strategy,
    int BridgeCount);

/// <summary>
/// Respuesta de POST .../vectors/{vectorId}/physical-union/confirm: la
/// unión SÍ se persistió como una <see cref="Vectify.Api.Vectorization.VectorVersion"/>
/// nueva -- `PreviousVectorId` (la capa de origen, que sigue existiendo
/// intacta en el historial) y `NewVectorId`/`SvgUrl` (el resultado fusionado,
/// servible por el endpoint YA existente GET .../vectors/{vectorId}).
/// </summary>
public sealed record PhysicalUnionConfirmResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid PreviousVectorId,
    Guid NewVectorId,
    string SvgUrl,
    int NewVectorVersion,
    int PhysicalUnionVersion,
    IReadOnlyList<string> ComponentIds,
    int Width,
    int Height,
    VectorMetricsPayload Metrics,
    int ComponentCountBefore,
    int ComponentCountAfter,
    string Strategy,
    int BridgeCount);
