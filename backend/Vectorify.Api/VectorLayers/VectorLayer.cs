namespace Vectorify.Api.VectorLayers;

/// <summary>
/// Una capa vectorial (M2-S02): liga explícitamente (a) el
/// <see cref="Vectorify.Api.ColorPalette.ColorGroup"/> de origen (color/
/// nombre/área heredados de la paleta confirmada, ver <see cref="GroupId"/>)
/// con (b) una <see cref="Vectorify.Api.Vectorization.VectorVersion"/> --
/// reutilizando el tipo YA EXISTENTE de M1-S05, no un tipo paralelo. Cada
/// capa ES una vectorización (su propio SVG, sus propias métricas, servida
/// por el endpoint YA EXISTENTE GET .../vectors/{vectorId}), con metadata
/// adicional de qué color/grupo representa. <see cref="VectorId"/> referencia
/// esa <see cref="Vectorify.Api.Vectorization.VectorVersion"/> en el mismo
/// <see cref="Vectorify.Api.Vectorization.IVectorVersionRegistry"/> que usa el
/// resto del pipeline (Simplification/Check/Dimension ya saben resolver un
/// VectorId explícito, así que una capa individual es, para el resto del
/// pipeline, indistinguible de cualquier otro SVG vectorizado).
///
/// <see cref="RasterValidation"/> (M2.1-S03, NUEVO): resultado -- ya
/// calculado por el motor Python al generar esta capa, ver
/// <see cref="LayerRasterValidation"/> -- de comparar la geometría vectorial
/// resultante contra su máscara raster de origen y contra las máscaras de
/// las demás capas de la misma paleta. Persistido junto al resto de la capa
/// para que el contrato consolidado (ver
/// Vectorify.Api.Endpoints.ConsolidatedVectorLayerEndpoints) pueda exponerlo
/// sin volver a llamar a Python.
/// </summary>
public sealed record VectorLayer(
    Guid GroupId,
    string Name,
    string ColorHex,
    double AreaPercent,
    bool HasPartialAlpha,
    Guid VectorId,
    LayerRasterValidation RasterValidation);
