namespace Vectorify.Api.Contracts;

/// <summary>
/// Validación raster-vs-vector de una capa (M2.1-S03) tal como la ve React --
/// ver Vectorify.Api.VectorLayers.LayerRasterValidation.
/// </summary>
public sealed record RasterValidationPayload(
    double OwnMismatchRatio,
    double OwnMismatchTolerance,
    bool OwnMismatchWithinTolerance,
    double ContaminationRatio,
    double ContaminationTolerance,
    bool ContaminationWithinTolerance,
    IReadOnlyList<string> Warnings);

/// <summary>
/// El modelo `VectorLayer` CONSOLIDADO (M2.1-S03, spec.md "Modelo mínimo":
/// "id, name, colorId, fill, visible, locked, order, manufacturingOperation,
/// paths[], componentCount"): combina, para cada capa, lo que hoy está
/// disperso en <see cref="VectorLayerPayload"/> (M2-S02/M2.1-S01),
/// <see cref="ComponentSetResponse"/> (M2-S03, solo `Components.Count`) y
/// <see cref="ManufacturingOperationPayload"/> (M2-S07) en una única
/// respuesta -- sin recalcular ninguno de los tres, solo componiéndolos (ver
/// Vectorify.Api.Endpoints.ConsolidatedVectorLayerEndpoints).
///
/// Decisiones de nombres/forma (documentadas en el reporte del sprint):
/// - `Id` = `GroupId` (spec.md lo pide literalmente como `id`).
/// - `ColorHex` (no `ColorId`): el codebase ya usa `ColorHex` en todo el
///   pipeline (ColorGroup/VectorLayerPayload/ManufacturingOperationPayload);
///   no existe ningún concepto de "id de color" separado del propio
///   `GroupId` -- introducir uno sería un tipo paralelo sin ningún consumidor.
/// - `Fill` = el mismo valor que `ColorHex`: `SvgFillWriter` (M2.1-S01) pinta
///   el SVG con `ColorHex` tal cual, así que hoy son literalmente el mismo
///   valor -- se expone igual para que el contrato coincida textualmente con
///   el campo que pide spec.md, sin implicar que puedan divergir.
/// - `SvgUrl` (no `Paths[]`): spec.md acepta explícitamente "paths[] O
///   referencia al SVG" -- se reutiliza el mismo criterio ya establecido por
///   `VectorLayerPayload.SvgUrl` (servir la geometría completa vía el
///   endpoint GET .../vectors/{vectorId} ya existente) en vez de duplicar/
///   re-serializar la geometría del `<path>` a JSON.
/// - `PathCount` (M2.1-S04, NUEVO): número de `&lt;path&gt;` del SVG de esta
///   capa -- lectura trivial de la MISMA `VectorVersion.Metrics` que ya
///   calculó/persistió M1-S05 al vectorizar (ver
///   <see cref="Vectorify.Api.Vectorization.IVectorizationService.FindVector"/>,
///   reutilizado tal cual, sin recalcular nada), expuesto acá porque
///   spec.md M2.1-S04 ("Información visible por layer") lo pide y hoy no
///   existe ningún otro lugar del frontend que lo muestre por capa.
/// - `ComponentCount` es `int?` (null si M2-S03 todavía no se calculó para
///   este VectorId -- es una operación separada, bajo demanda) en vez de
///   forzar un 0 engañoso.
/// - `ManufacturingOperation` es el valor de wire ya establecido por M2-S07
///   ("cut"/"engrave"/"ignore"/"unassigned").
/// - `Visible`/`Locked`/`Order`: ver spec.md, "Ambigüedades detectadas" --
///   esta tarjeta expone valores DEFAULT/COMPUTADOS (`Visible=true`,
///   `Locked=false`, `Order=`posición en `Layers`, estable mientras no
///   cambie el conjunto de capas) sin persistencia interactiva nueva; la
///   persistencia real de estos 3 campos es responsabilidad de M2.1-S07
///   ("Persists order, name, visible, locked, and operation").
/// </summary>
public sealed record ConsolidatedVectorLayerPayload(
    Guid Id,
    string Name,
    string ColorHex,
    string Fill,
    Guid VectorId,
    string SvgUrl,
    int PathCount,
    int? ComponentCount,
    string ManufacturingOperation,
    bool Visible,
    bool Locked,
    int Order,
    RasterValidationPayload RasterValidation);

/// <summary>
/// Respuesta de GET .../color-palette/{paletteId}/layers/consolidated: la
/// ÚLTIMA versión vigente del conjunto de capas (M2-S02), con cada capa ya
/// combinada con su componentCount/manufacturingOperation/visible/locked/
/// order -- ver <see cref="ConsolidatedVectorLayerPayload"/>.
/// </summary>
public sealed record ConsolidatedVectorLayerSetResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid LayerSetId,
    int Version,
    Guid PaletteId,
    int PaletteVersion,
    int SourceWidthPx,
    int SourceHeightPx,
    IReadOnlyList<ConsolidatedVectorLayerPayload> Layers);
