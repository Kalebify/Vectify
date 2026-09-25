namespace Vectify.Api.Dimensioning;

/// <summary>Resultado de APLICAR (persistir) dimensiones físicas: nueva versión (recién generada) o cacheada.</summary>
public abstract record DimensionResult
{
    private DimensionResult()
    {
    }

    /// <summary>Dimensiones aplicadas: nuevas (recién generadas) o cacheadas (mismo SVG de origen + mismas dimensiones ya vistas).</summary>
    public sealed record Ready(DimensionVersion Record, bool FromCache) : DimensionResult;

    /// <summary>No existe un proyecto/imagen con esos IDs, o el sourceId de origen no existe para esa imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : DimensionResult;

    /// <summary>Los parámetros están fuera de rango, faltan, o son incoherentes con LockAspectRatio (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : DimensionResult;

    /// <summary>El storage falló, o el SVG de origen ya guardado no es válido, de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : DimensionResult;
}
