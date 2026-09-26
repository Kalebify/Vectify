namespace Vectify.Api.Contracts;

/// <summary>
/// Una capa vista a través de su intención de fabricación: hereda color/
/// nombre del conjunto de capas (M2-S02, VectorLayerPayload) para que React
/// pueda mostrar la leyenda sin tener que cruzar dos respuestas distintas.
/// `Operation` es "cut" | "engrave" | "ignore" | "unassigned" -- este último
/// valor SOLO aparece en respuestas (nunca es válido como entrada), y
/// significa que esa capa nunca recibió una asignación explícita (ver
/// spec.md, "Ambigüedades detectadas": nunca se asume "Corte" por defecto).
/// </summary>
public sealed record ManufacturingOperationPayload(
    Guid GroupId,
    string Name,
    string ColorHex,
    string Operation);

/// <summary>Conteo agregado de la última versión del conjunto de asignaciones -- pensado para mostrarse "antes de exportar" (ver spec.md).</summary>
public sealed record ManufacturingOperationSummaryPayload(
    int CutCount,
    int EngraveCount,
    int IgnoreCount,
    int UnassignedCount,
    int TotalCount);

/// <summary>
/// Respuesta de POST .../layers/{groupId}/operation y GET .../layers/operations:
/// siempre la última versión vigente del conjunto de asignaciones para el
/// conjunto de capas ACTUAL de la paleta (una entrada por cada capa, incluidas
/// las que todavía no tienen ninguna asignación explícita -- "unassigned").
/// `Version` es 0 (con todas las capas en "unassigned") si esa paleta+versión
/// confirmada nunca recibió ninguna asignación.
/// </summary>
public sealed record ManufacturingOperationSetResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid PaletteId,
    int PaletteVersion,
    Guid LayerSetId,
    int Version,
    IReadOnlyList<ManufacturingOperationPayload> Operations,
    ManufacturingOperationSummaryPayload Summary);
