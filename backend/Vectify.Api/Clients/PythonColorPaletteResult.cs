namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de pedirle una detección de paleta de colores al motor Python.</summary>
public enum PythonColorPaletteState
{
    /// <summary>Python devolvió 200 con una paleta detectada correctamente.</summary>
    Success,

    /// <summary>Python devolvió 400: la imagen no se pudo decodificar.</summary>
    CorruptImage,

    /// <summary>Python devolvió 413: la imagen excede el tamaño máximo.</summary>
    DimensionsExceeded,

    /// <summary>Python devolvió 422 por parámetros inválidos (defensa en profundidad; Vectify.Api ya validó antes).</summary>
    InvalidParameters,

    /// <summary>Python devolvió 504, o la solicitud excedió el tiempo configurado (ColorPalette:TimeoutSeconds).</summary>
    Timeout,

    /// <summary>Python devolvió 500: la detección falló de una forma inesperada.</summary>
    EngineError,

    /// <summary>No se pudo establecer conexión con el motor Python.</summary>
    Unavailable,

    /// <summary>Python respondió pero el cuerpo no es JSON válido o le faltan campos esperados.</summary>
    InvalidResponse,

    /// <summary>
    /// Python respondió 200 con JSON bien formado y los campos esperados presentes, pero el
    /// contenido no pasa la validación defensiva adicional del cliente (color_hex mal formado,
    /// porcentajes/dimensiones fuera de rango, masks no decodificables, Content-Type inesperado,
    /// la suma de pixel_count de los grupos no coincide con lo esperado) -- mismo criterio de
    /// defensa en profundidad que PythonSimplifyClient.InvalidSvg.
    /// </summary>
    InvalidPaletteResponse,

    /// <summary>Python respondió con un código de error HTTP no contemplado arriba.</summary>
    HttpError,
}

public sealed record PythonColorGroupResult(
    int Id, string ColorHex, long PixelCount, double AreaPercent, bool HasPartialAlpha, byte[] MaskBytes);

public sealed record PythonColorPaletteResult(
    PythonColorPaletteState State,
    int? Width,
    int? Height,
    string? ContentType,
    IReadOnlyList<PythonColorGroupResult>? Groups,
    double? TransparentPercent,
    byte[]? QuantizedPreviewBytes,
    string? Message);
