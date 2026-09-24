namespace Vectify.Api.Threshold;

/// <summary>Resultado de intentar generar (o recuperar del cache) una máscara de threshold.</summary>
public abstract record ThresholdResult
{
    private ThresholdResult()
    {
    }

    /// <summary>Máscara disponible: nueva (recién generada) o cacheada (mismo preview de origen + parámetros ya vistos).</summary>
    public sealed record Ready(ThresholdConfigRecord Record, bool FromCache) : ThresholdResult;

    /// <summary>No existe un proyecto/imagen con esos IDs, o el previewId de origen no existe para esa imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : ThresholdResult;

    /// <summary>Los parámetros están fuera de rango o faltan (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : ThresholdResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : ThresholdResult;
}
