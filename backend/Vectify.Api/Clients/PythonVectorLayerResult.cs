using Vectify.Api.Vectorization;

namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de pedirle al motor Python el conjunto completo de capas.</summary>
public enum PythonVectorLayerState
{
    /// <summary>Python devolvió 200 con una capa por cada máscara enviada.</summary>
    Success,

    /// <summary>Python devolvió 400: alguna máscara es corrupta o no se pudo decodificar.</summary>
    CorruptImage,

    /// <summary>Python devolvió 413: alguna máscara excede las dimensiones máximas, o algún SVG resultante excede el tamaño máximo.</summary>
    DimensionsExceeded,

    /// <summary>Python devolvió 422: alguna máscara no tiene ningún píxel de foreground (vacía).</summary>
    EmptyMask,

    /// <summary>Python devolvió 422 por parámetros inválidos (ej. group_ids no coincide con la cantidad de archivos).</summary>
    InvalidParameters,

    /// <summary>Python devolvió 504, o la solicitud excedió el tiempo configurado (VectorLayer:TimeoutSeconds).</summary>
    Timeout,

    /// <summary>Python devolvió 500: el motor de trazado falló de una forma inesperada en alguna máscara.</summary>
    EngineError,

    /// <summary>No se pudo establecer conexión con el motor Python.</summary>
    Unavailable,

    /// <summary>Python respondió pero el cuerpo no es JSON válido o le faltan campos esperados.</summary>
    InvalidResponse,

    /// <summary>
    /// Python respondió 200 con JSON bien formado, pero el contenido no pasa la validación
    /// defensiva adicional del cliente (cantidad de capas distinta a la cantidad de máscaras
    /// enviadas, group_id no reconocido, SVG no es XML bien formado, dimensiones/métricas no
    /// positivas, bounds no finitos/incoherentes, o Content-Type inesperado) -- mismo criterio
    /// de defensa en profundidad que PythonVectorizeClient.
    /// </summary>
    InvalidSvg,

    /// <summary>Python respondió con un código de error HTTP no contemplado arriba.</summary>
    HttpError,
}

/// <summary>Una capa ya validada y decodificada, lista para persistir como <see cref="Vectify.Api.Vectorization.VectorVersion"/>.</summary>
public sealed record PythonVectorLayerItemResult(
    Guid GroupId,
    string Svg,
    string ContentType,
    int Width,
    int Height,
    VectorMetrics Metrics);

public sealed record PythonVectorLayerBatchResult(
    PythonVectorLayerState State,
    IReadOnlyList<PythonVectorLayerItemResult>? Layers,
    string? Message);
