namespace Vectify.Api.Components;

/// <summary>
/// Una versión guardada del análisis de componentes físicos de UNA capa
/// vectorial (M2-S03), junto con la referencia a la
/// <see cref="Vectify.Api.Vectorization.VectorVersion"/> de origen
/// (<see cref="VectorId"/>) sobre la que se calculó. Cada
/// <see cref="Vectify.Api.Vectorization.VectorVersion"/> es INMUTABLE (una
/// vez generada, su SVG nunca cambia -- ver M1-S05/M2-S02), así que
/// <see cref="VectorId"/> por sí solo alcanza como clave de caché: no hay
/// otro parámetro ajustable en este sprint (las tolerancias de "tocarse"/
/// "diminuto" son fijas del lado del motor Python, ver
/// app.core.config.Settings) -- mismo criterio de historial versionado
/// (nunca se muta una versión existente, cache-hit avanza la versión) que
/// VectorVersion/DimensionVersion/VectorLayerSetVersion.
/// </summary>
public sealed record ComponentSetVersion(
    Guid ProjectId,
    Guid ImageId,
    int Version,
    Guid ComponentSetId,
    Guid VectorId,
    IReadOnlyList<LayerComponent> Components,
    int SkippedPathCount,
    DateTimeOffset CreatedAt);
