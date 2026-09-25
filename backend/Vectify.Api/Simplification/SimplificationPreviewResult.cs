namespace Vectify.Api.Simplification;

/// <summary>
/// Resultado de pedir un PREVIEW de simplificación: nunca toca
/// ISimplificationVersionRegistry ni crea una nueva versión -- ver spec.md,
/// criterios de aceptación: "Preview es reversible: cancelar no deja rastro
/// (no crea versión, no modifica el estado persistido)". El SVG resultante
/// viaja completo en <see cref="Ready.Svg"/> para que React pueda renderizar
/// la comparación antes/después sin que la Web API haya persistido nada.
/// </summary>
public abstract record SimplificationPreviewResult
{
    private SimplificationPreviewResult()
    {
    }

    public sealed record Ready(
        string Svg, string ContentType, int Width, int Height, SimplificationMetrics Metrics, SimplificationParameters Parameters)
        : SimplificationPreviewResult;

    /// <summary>No existe un proyecto/imagen con esos IDs, o el vectorId de origen no existe para esa imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : SimplificationPreviewResult;

    /// <summary>Los parámetros están fuera de rango o faltan (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : SimplificationPreviewResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : SimplificationPreviewResult;
}
