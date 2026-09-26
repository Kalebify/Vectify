using Vectify.Api.Vectorization;

namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de pedirle una unión física de piezas al motor Python.</summary>
public enum PythonPhysicalUnionState
{
    /// <summary>Python devolvió 200 con el SVG fusionado, YA revalidado como la cantidad de componentes esperada.</summary>
    Success,

    /// <summary>Python devolvió 400: el SVG de entrada es corrupto/no decodificable o no es XML/SVG válido.</summary>
    InvalidInputSvg,

    /// <summary>Python devolvió 413: el SVG de entrada excede el tamaño máximo, o tiene demasiados subpaths analizables.</summary>
    SvgTooLarge,

    /// <summary>Python devolvió 422 por parámetros/selección inválidos (menos de 2 componentes, IDs repetidos, subpath referenciado dos veces, etc.).</summary>
    InvalidParameters,

    /// <summary>
    /// Python devolvió 422 (physical_union_invalid_geometry): uno o más componentes
    /// seleccionados tiene geometría autointersectante o degenerada, rechazada
    /// explícitamente en vez de "arreglada" en silencio.
    /// </summary>
    InvalidGeometry,

    /// <summary>
    /// Python devolvió 422 (physical_union_impossible): la validación post-operación no
    /// negociable ("nunca fingir unión") detectó que el resultado no se reanaliza como la
    /// cantidad de componentes esperada -- la unión no fue geométricamente posible.
    /// </summary>
    Impossible,

    /// <summary>Python devolvió 504, o la solicitud excedió el tiempo configurado (PhysicalUnion:TimeoutSeconds).</summary>
    Timeout,

    /// <summary>Python devolvió 500: fallo inesperado del motor.</summary>
    EngineError,

    /// <summary>No se pudo establecer conexión con el motor Python.</summary>
    Unavailable,

    /// <summary>Python respondió pero el cuerpo no es JSON válido, le faltan campos esperados, o no pasa la validación defensiva adicional del cliente.</summary>
    InvalidResponse,

    /// <summary>Python respondió con un código de error HTTP no contemplado arriba.</summary>
    HttpError,
}

public sealed record PythonPhysicalUnionResult(
    PythonPhysicalUnionState State,
    string? Svg,
    string? ContentType,
    int? Width,
    int? Height,
    VectorMetrics? Metrics,
    int? ComponentCountBefore,
    int? ComponentCountAfter,
    string? Strategy,
    int? BridgeCount,
    string? Message);
