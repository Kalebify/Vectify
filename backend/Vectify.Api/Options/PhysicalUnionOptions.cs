namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la unión física de piezas (M2-S06): timeout HTTP hacia
/// el motor Python. Mismo criterio que <see cref="ComponentOptions"/>: sin
/// tolerancias ajustables desde Vectify.Api en este sprint (touch_ratio/
/// tiny_area_ratio/bridge_width_ratio los aplica Python con sus propios
/// defaults, ver app.core.config.Settings del lado del motor Python) -- no
/// hay UI para ajustarlas, solo seleccionar componentes y confirmar/cancelar.
/// Se enlaza desde la sección "PhysicalUnion".
/// </summary>
public sealed class PhysicalUnionOptions
{
    public const string SectionName = "PhysicalUnion";

    /// <summary>
    /// Más alto que Component:TimeoutSeconds: la unión física, a diferencia
    /// del análisis de solo lectura de M2-S03, corre operaciones booleanas/
    /// bridging con Shapely/GEOS Y una segunda pasada completa del
    /// analizador de componentes como validación post-operación (ver
    /// app.core.physical_union) -- más trabajo por request.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
