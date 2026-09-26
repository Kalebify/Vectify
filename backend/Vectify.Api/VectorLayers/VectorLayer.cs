namespace Vectify.Api.VectorLayers;

/// <summary>
/// Una capa vectorial (M2-S02): liga explícitamente (a) el
/// <see cref="Vectify.Api.ColorPalette.ColorGroup"/> de origen (color/
/// nombre/área heredados de la paleta confirmada, ver <see cref="GroupId"/>)
/// con (b) una <see cref="Vectify.Api.Vectorization.VectorVersion"/> --
/// reutilizando el tipo YA EXISTENTE de M1-S05, no un tipo paralelo. Cada
/// capa ES una vectorización (su propio SVG, sus propias métricas, servida
/// por el endpoint YA EXISTENTE GET .../vectors/{vectorId}), con metadata
/// adicional de qué color/grupo representa. <see cref="VectorId"/> referencia
/// esa <see cref="Vectify.Api.Vectorization.VectorVersion"/> en el mismo
/// <see cref="Vectify.Api.Vectorization.IVectorVersionRegistry"/> que usa el
/// resto del pipeline (Simplification/Check/Dimension ya saben resolver un
/// VectorId explícito, así que una capa individual es, para el resto del
/// pipeline, indistinguible de cualquier otro SVG vectorizado).
/// </summary>
public sealed record VectorLayer(
    Guid GroupId,
    string Name,
    string ColorHex,
    double AreaPercent,
    bool HasPartialAlpha,
    Guid VectorId);
