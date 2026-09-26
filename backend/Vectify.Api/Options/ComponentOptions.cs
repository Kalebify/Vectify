namespace Vectify.Api.Options;

/// <summary>
/// Configuración del análisis de componentes físicos independientes por
/// capa (M2-S03): timeout HTTP hacia el motor Python. A diferencia de
/// Check/ColorPalette, NO incluye tolerancias ajustables -- las tolerancias
/// de "tocarse"/"componente diminuto" no son configurables desde
/// Vectify.Api en este sprint (Python aplica sus propios defaults, ver
/// app.core.config.Settings del lado del motor Python; no hay UI para
/// ajustarlas, ver spec.md M2-S03: "Usuario podrá": solo ver/seleccionar
/// componentes, no ajustar tolerancias). Se enlaza desde la sección
/// "Component".
/// </summary>
public sealed class ComponentOptions
{
    public const string SectionName = "Component";

    public int TimeoutSeconds { get; set; } = 20;
}
