using Vectify.Api.Checking;

namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de pedirle un análisis de paths abiertos/duplicados al motor Python.</summary>
public enum PythonCheckState
{
    /// <summary>Python devolvió 200 con los issues detectados.</summary>
    Success,

    /// <summary>Python devolvió 400: el SVG de entrada no es UTF-8/XML válido o no tiene <svg> como raíz.</summary>
    InvalidInputSvg,

    /// <summary>Python devolvió 413: el SVG de entrada excede el tamaño máximo.</summary>
    SvgTooLarge,

    /// <summary>Python devolvió 413: el SVG tiene más subpaths analizables que el límite configurado (salvaguarda O(n^2)).</summary>
    TooManySubpaths,

    /// <summary>Python devolvió 422 por parámetros inválidos (defensa en profundidad; Vectify.Api ya validó antes).</summary>
    InvalidParameters,

    /// <summary>Python devolvió 504, o la solicitud excedió el tiempo configurado (Check:TimeoutSeconds).</summary>
    Timeout,

    /// <summary>Python devolvió 500: el análisis falló de una forma inesperada.</summary>
    EngineError,

    /// <summary>No se pudo establecer conexión con el motor Python.</summary>
    Unavailable,

    /// <summary>Python respondió pero el cuerpo no es JSON válido o le faltan campos esperados.</summary>
    InvalidResponse,

    /// <summary>
    /// Python respondió 200 con JSON bien formado y los campos esperados presentes, pero el
    /// contenido no pasa la validación defensiva adicional del cliente (issues con forma
    /// incoherente, conteos de summary que no coinciden con la cantidad real de issues,
    /// severidades desconocidas, tolerancias efectivas fuera de rango) -- mismo criterio de
    /// defensa en profundidad que PythonSimplifyClient.InvalidSvg.
    /// </summary>
    InvalidSvg,

    /// <summary>Python respondió con un código de error HTTP no contemplado arriba.</summary>
    HttpError,
}

public sealed record PythonCheckResult(
    PythonCheckState State,
    IReadOnlyList<CheckIssue>? Issues,
    int? SkippedPathCount,
    string? Message);
