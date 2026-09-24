namespace Vectify.Api.Preprocessing;

/// <summary>Resultado de intentar generar (o recuperar del cache) un preview preprocesado.</summary>
public abstract record PreprocessResult
{
    private PreprocessResult()
    {
    }

    /// <summary>Preview disponible: nuevo (recién generado) o cacheado (mismos parámetros ya vistos).</summary>
    public sealed record Ready(PreprocessConfigRecord Record, bool FromCache) : PreprocessResult;

    /// <summary>No existe un proyecto/imagen con esos IDs (el endpoint responde 404).</summary>
    public sealed record NotFound : PreprocessResult;

    /// <summary>Los parámetros están fuera de rango (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : PreprocessResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : PreprocessResult;
}
