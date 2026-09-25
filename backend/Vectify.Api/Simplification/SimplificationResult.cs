namespace Vectify.Api.Simplification;

/// <summary>Resultado de APLICAR (persistir) una simplificación: nueva versión (recién generada) o cacheada.</summary>
public abstract record SimplificationResult
{
    private SimplificationResult()
    {
    }

    /// <summary>Simplificación disponible: nueva (recién generada) o cacheada (mismo SVG de origen + parámetros ya vistos).</summary>
    public sealed record Ready(SimplificationVersion Record, bool FromCache) : SimplificationResult;

    /// <summary>No existe un proyecto/imagen con esos IDs, o el vectorId de origen no existe para esa imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : SimplificationResult;

    /// <summary>Los parámetros están fuera de rango o faltan (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : SimplificationResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : SimplificationResult;
}
