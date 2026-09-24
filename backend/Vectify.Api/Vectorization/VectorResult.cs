namespace Vectify.Api.Vectorization;

/// <summary>Resultado de intentar generar (o recuperar del cache) una vectorización.</summary>
public abstract record VectorResult
{
    private VectorResult()
    {
    }

    /// <summary>Vectorización disponible: nueva (recién generada) o cacheada (misma máscara de origen + parámetros ya vistos).</summary>
    public sealed record Ready(VectorVersion Record, bool FromCache) : VectorResult;

    /// <summary>No existe un proyecto/imagen con esos IDs, o el maskId de origen no existe para esa imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : VectorResult;

    /// <summary>Los parámetros están fuera de rango o faltan (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : VectorResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : VectorResult;
}
