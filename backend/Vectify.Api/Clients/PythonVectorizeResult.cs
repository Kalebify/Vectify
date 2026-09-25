using Vectify.Api.Vectorization;

namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de pedirle una vectorización al motor Python.</summary>
public enum PythonVectorizeState
{
    /// <summary>Python devolvió 200 con un SVG generado correctamente.</summary>
    Success,

    /// <summary>Python devolvió 400: la máscara es corrupta o no se pudo decodificar.</summary>
    CorruptImage,

    /// <summary>Python devolvió 413: la máscara excede las dimensiones máximas, o el SVG resultante excede el tamaño máximo de salida.</summary>
    DimensionsExceeded,

    /// <summary>Python devolvió 422: la máscara no tiene ningún píxel de foreground (vacía).</summary>
    EmptyMask,

    /// <summary>Python devolvió 422 por parámetros inválidos (defensa en profundidad; hoy no hay parámetros ajustables que enviar).</summary>
    InvalidParameters,

    /// <summary>Python devolvió 504, o la solicitud excedió el tiempo configurado (Vectorize:TimeoutSeconds).</summary>
    Timeout,

    /// <summary>Python devolvió 500: el motor de trazado falló de una forma inesperada.</summary>
    EngineError,

    /// <summary>No se pudo establecer conexión con el motor Python.</summary>
    Unavailable,

    /// <summary>Python respondió pero el cuerpo no es JSON válido o le faltan campos esperados.</summary>
    InvalidResponse,

    /// <summary>
    /// Python respondió 200 con JSON bien formado y los campos esperados presentes, pero
    /// el contenido no pasa la validación defensiva adicional del cliente (SVG no es XML
    /// bien formado con &lt;svg&gt; como raíz, dimensiones/métricas no positivas, bounds no
    /// finitos o incoherentes, Content-Type inesperado, o el SVG excede
    /// Vectorize:MaxSvgResponseBytes) -- nunca debería ocurrir con VTracer/svg_processing.py
    /// del lado Python (ya sanitiza y valida), pero Vectify.Api nunca confía ciegamente en
    /// su caller (defensa en profundidad, ver Defecto 3 de la ronda de QA sobre M1-S05/M1-S06).
    /// </summary>
    InvalidSvg,

    /// <summary>Python respondió con un código de error HTTP no contemplado arriba.</summary>
    HttpError,
}

public sealed record PythonVectorizeResult(
    PythonVectorizeState State,
    string? Svg,
    string? ContentType,
    int? Width,
    int? Height,
    VectorMetrics? Metrics,
    string? Message);
