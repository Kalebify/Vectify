using Vectify.Api.Simplification;

namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de pedirle una simplificación de nodos al motor Python.</summary>
public enum PythonSimplifyState
{
    /// <summary>Python devolvió 200 con un SVG simplificado correctamente.</summary>
    Success,

    /// <summary>Python devolvió 400: el SVG de entrada no es UTF-8/XML válido o no tiene <svg> como raíz.</summary>
    InvalidInputSvg,

    /// <summary>Python devolvió 413: el SVG de entrada (o el resultante) excede el tamaño máximo.</summary>
    SvgTooLarge,

    /// <summary>Python devolvió 422 por parámetros inválidos (defensa en profundidad; Vectify.Api ya validó antes).</summary>
    InvalidParameters,

    /// <summary>Python devolvió 504, o la solicitud excedió el tiempo configurado (Simplification:TimeoutSeconds).</summary>
    Timeout,

    /// <summary>Python devolvió 500: la simplificación falló de una forma inesperada.</summary>
    EngineError,

    /// <summary>No se pudo establecer conexión con el motor Python.</summary>
    Unavailable,

    /// <summary>Python respondió pero el cuerpo no es JSON válido o le faltan campos esperados.</summary>
    InvalidResponse,

    /// <summary>
    /// Python respondió 200 con JSON bien formado y los campos esperados presentes, pero el
    /// contenido no pasa la validación defensiva adicional del cliente (SVG no es XML bien
    /// formado con &lt;svg&gt; como raíz, métricas negativas o incoherentes -- ej. más nodos
    /// después que antes --, % de reducción fuera de [0,100], bounds no finitos, Content-Type
    /// inesperado, o el SVG excede Simplification:MaxSvgResponseBytes) -- mismo criterio de
    /// defensa en profundidad que PythonVectorizeClient.InvalidSvg.
    /// </summary>
    InvalidSvg,

    /// <summary>Python respondió con un código de error HTTP no contemplado arriba.</summary>
    HttpError,
}

public sealed record PythonSimplifyResult(
    PythonSimplifyState State,
    string? Svg,
    string? ContentType,
    SimplificationMetrics? Metrics,
    string? Message);
