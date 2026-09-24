using Vectify.Api.Preprocessing;

namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de pedirle un preview al motor Python.</summary>
public enum PythonPreprocessState
{
    /// <summary>Python devolvió 200 con un preview generado correctamente.</summary>
    Success,

    /// <summary>Python devolvió 400: la imagen es corrupta o no se pudo decodificar.</summary>
    CorruptImage,

    /// <summary>Python devolvió 413: la imagen excede las dimensiones máximas permitidas.</summary>
    DimensionsExceeded,

    /// <summary>Python devolvió 422: rechazó los parámetros (defensa en profundidad; la Web API ya validó antes de llamar).</summary>
    InvalidParameters,

    /// <summary>La solicitud excedió el tiempo configurado (Preprocess:TimeoutSeconds).</summary>
    Timeout,

    /// <summary>No se pudo establecer conexión con el motor Python.</summary>
    Unavailable,

    /// <summary>Python respondió pero el cuerpo no es JSON válido o le faltan campos esperados.</summary>
    InvalidResponse,

    /// <summary>Python respondió con un código de error HTTP no contemplado arriba (por ejemplo, 500).</summary>
    HttpError,
}

public sealed record PythonPreprocessResult(
    PythonPreprocessState State,
    byte[]? ImageBytes,
    string? ContentType,
    int? Width,
    int? Height,
    int? OriginalWidth,
    int? OriginalHeight,
    PreprocessParameters? EffectiveParameters,
    PreprocessMetrics? Metrics,
    string? Message);
