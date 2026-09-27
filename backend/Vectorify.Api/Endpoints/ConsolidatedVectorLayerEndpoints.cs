using Vectorify.Api.Components;
using Vectorify.Api.Contracts;
using Vectorify.Api.ManufacturingOperations;
using Vectorify.Api.VectorLayers;

namespace Vectorify.Api.Endpoints;

/// <summary>
/// Endpoint del modelo `VectorLayer` CONSOLIDADO (M2.1-S03): combina, para
/// cada capa del conjunto vigente de una paleta confirmada (M2-S02), lo que
/// hoy está disperso en tres servicios separados --
/// <see cref="IVectorLayerService"/> (color/fill/SVG, M2-S02/M2.1-S01),
/// <see cref="IComponentAnalysisService"/> (componentCount, M2-S03) y
/// <see cref="IManufacturingOperationService"/> (operación de fabricación,
/// M2-S07) -- en una única respuesta, SIN recalcular ninguno de los tres
/// (solo lectura de lo ya persistido por cada uno, mismo criterio de
/// composición fina en el endpoint que ya usa
/// <see cref="ManufacturingOperationEndpoints"/> al combinar
/// VectorLayerSetVersion + ManufacturingOperationSetVersion).
///
/// Deliberadamente un endpoint NUEVO (no una modificación del contrato ya
/// existente de <see cref="VectorLayerEndpoints"/>, del que ya dependen otras
/// tarjetas, como M2-S07/ManufacturingOperationEndpoints): aditivo, sin
/// riesgo de romper ningún consumidor existente de GET/POST .../layers.
/// </summary>
public static class ConsolidatedVectorLayerEndpoints
{
    public static void MapConsolidatedVectorLayerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/v1/projects/{projectId:guid}/images/{imageId:guid}/color-palette/{paletteId:guid}/layers/consolidated",
            (
                Guid projectId,
                Guid imageId,
                Guid paletteId,
                IVectorLayerService vectorLayerService,
                IComponentAnalysisService componentAnalysisService,
                IManufacturingOperationService manufacturingOperationService) =>
            {
                var layerSet = vectorLayerService.FindLatest(projectId, imageId, paletteId);
                if (layerSet is null)
                {
                    return Results.NotFound(
                        new ApiErrorResponse("not_found", "No existe un conjunto de capas (M2-S02) generado para esa paleta."));
                }

                var assignments = manufacturingOperationService.FindCurrent(projectId, imageId, paletteId)?.Assignments;
                var operationByGroupId = (assignments?.Assignments ?? Array.Empty<ManufacturingOperationAssignment>())
                    .ToDictionary(a => a.GroupId, a => a.Operation);

                var layers = layerSet.Layers
                    .Select((layer, index) => ToPayload(projectId, imageId, layer, index, componentAnalysisService, operationByGroupId))
                    .ToList();

                return Results.Ok(new ConsolidatedVectorLayerSetResponse(
                    layerSet.ProjectId,
                    layerSet.ImageId,
                    layerSet.LayerSetId,
                    layerSet.Version,
                    layerSet.PaletteId,
                    layerSet.PaletteVersion,
                    layerSet.SourceWidthPx,
                    layerSet.SourceHeightPx,
                    layers));
            })
        .WithName("GetConsolidatedVectorLayers")
        .WithTags("VectorLayers")
        .Produces<ConsolidatedVectorLayerSetResponse>(StatusCodes.Status200OK)
        .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
        .WithSummary(
            "Recupera el conjunto de capas vigente de una paleta con el modelo VectorLayer " +
            "CONSOLIDADO (M2.1-S03): color/fill/geometría, componentCount (si ya se calculó, M2-S03), " +
            "manufacturingOperation (M2-S07, 'unassigned' si no se asignó), y visible/locked/order " +
            "(valores default/computados -- la persistencia interactiva de esos 3 campos es de M2.1-S07).")
        .WithDescription(
            "Precondición: debe existir un conjunto de capas generado para esa paleta (POST " +
            ".../layers de M2-S02); si no, responde 404 sin llamar a ningún servicio adicional. " +
            "NUNCA dispara un cálculo de componentes ni una llamada a Python: componentCount es " +
            "null si esa capa (VectorId) todavía no tiene un análisis de componentes calculado.");
    }

    private static ConsolidatedVectorLayerPayload ToPayload(
        Guid projectId,
        Guid imageId,
        VectorLayer layer,
        int order,
        IComponentAnalysisService componentAnalysisService,
        IReadOnlyDictionary<Guid, ManufacturingOperationKind> operationByGroupId)
    {
        var componentCount = componentAnalysisService.FindLatest(projectId, imageId, layer.VectorId)?.Components.Count;
        var operation = operationByGroupId.TryGetValue(layer.GroupId, out var kind)
            ? ManufacturingOperationParser.ToWireValue(kind)
            : ManufacturingOperationParser.UnassignedWireValue;

        return new ConsolidatedVectorLayerPayload(
            Id: layer.GroupId,
            Name: layer.Name,
            ColorHex: layer.ColorHex,
            Fill: layer.ColorHex,
            VectorId: layer.VectorId,
            SvgUrl: $"/api/v1/projects/{projectId}/images/{imageId}/vectors/{layer.VectorId}",
            ComponentCount: componentCount,
            ManufacturingOperation: operation,
            Visible: true,
            Locked: false,
            Order: order,
            RasterValidation: new RasterValidationPayload(
                layer.RasterValidation.OwnMismatchRatio,
                layer.RasterValidation.OwnMismatchTolerance,
                layer.RasterValidation.OwnMismatchWithinTolerance,
                layer.RasterValidation.ContaminationRatio,
                layer.RasterValidation.ContaminationTolerance,
                layer.RasterValidation.ContaminationWithinTolerance,
                layer.RasterValidation.Warnings));
    }
}
