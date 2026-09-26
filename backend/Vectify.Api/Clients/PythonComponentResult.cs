using Vectify.Api.Components;

namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de pedirle un análisis de componentes físicos al motor Python.</summary>
public enum PythonComponentState
{
    /// <summary>Python devolvió 200 con los componentes detectados.</summary>
    Success,

    /// <summary>Python devolvió 400: el SVG de entrada no es UTF-8/XML válido o no tiene <![CDATA[<svg>]]> como raíz.</summary>
    InvalidInputSvg,

    /// <summary>Python devolvió 413: el SVG de entrada excede el tamaño máximo.</summary>
    SvgTooLarge,

    /// <summary>Python devolvió 413: el SVG tiene más subpaths analizables que el límite configurado (salvaguarda O(n^2)).</summary>
    TooManySubpaths,

    /// <summary>Python devolvió 422 por parámetros inválidos (defensa en profundidad; Vectify.Api siempre envía los defaults).</summary>
    InvalidParameters,

    /// <summary>Python devolvió 504, o la solicitud excedió el tiempo configurado (Component:TimeoutSeconds).</summary>
    Timeout,

    /// <summary>Python devolvió 500: el análisis falló de una forma inesperada.</summary>
    EngineError,

    /// <summary>No se pudo establecer conexión con el motor Python.</summary>
    Unavailable,

    /// <summary>Python respondió pero el cuerpo no es JSON válido o le faltan campos esperados.</summary>
    InvalidResponse,

    /// <summary>
    /// Python respondió 200 con JSON bien formado y los campos esperados presentes, pero el
    /// contenido no pasa la validación defensiva adicional del cliente (componentes con forma
    /// incoherente, conteos de summary que no coinciden con la cantidad real de componentes,
    /// roles desconocidos) -- mismo criterio de defensa en profundidad que PythonCheckClient.
    /// </summary>
    InvalidSvg,

    /// <summary>Python respondió con un código de error HTTP no contemplado arriba.</summary>
    HttpError,
}

public sealed record PythonComponentResult(
    PythonComponentState State,
    IReadOnlyList<LayerComponent>? Components,
    int? SkippedPathCount,
    string? Message);
