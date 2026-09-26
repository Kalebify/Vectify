namespace Vectify.Api.Components;

/// <summary>Resultado de intentar calcular (o recuperar del cache) el análisis de componentes físicos de una capa.</summary>
public abstract record ComponentSetResult
{
    private ComponentSetResult()
    {
    }

    /// <summary>Análisis disponible: recién calculado, o cacheado (mismo VectorId ya visto).</summary>
    public sealed record Ready(ComponentSetVersion Record, bool FromCache) : ComponentSetResult;

    /// <summary>No existe una capa vectorial (VectorVersion) con ese VectorId para esta imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : ComponentSetResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : ComponentSetResult;
}
