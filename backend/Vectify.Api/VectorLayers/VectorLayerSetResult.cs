namespace Vectify.Api.VectorLayers;

/// <summary>Resultado de intentar generar (o recuperar del cache) el conjunto completo de capas de una paleta confirmada.</summary>
public abstract record VectorLayerSetResult
{
    private VectorLayerSetResult()
    {
    }

    /// <summary>Conjunto de capas disponible: recién generado, o cacheado (misma paleta+versión confirmada ya vistas).</summary>
    public sealed record Ready(VectorLayerSetVersion Record, bool FromCache) : VectorLayerSetResult;

    /// <summary>No existe una sesión de paleta de colores con ese ID para esta imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : VectorLayerSetResult;

    /// <summary>
    /// La paleta existe pero todavía no está confirmada (precondición de M2-S02: solo se
    /// generan capas a partir de una ColorPaletteVersion con IsConfirmed=true) -- el
    /// endpoint responde 409, sin haber llamado a Python.
    /// </summary>
    public sealed record Conflict(string Code, string Message) : VectorLayerSetResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : VectorLayerSetResult;
}
