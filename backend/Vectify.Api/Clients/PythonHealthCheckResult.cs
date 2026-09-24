namespace Vectify.Api.Clients;

/// <summary>Resultado tipado, sin excepciones, de intentar consultar la salud del motor Python.</summary>
public enum PythonHealthState
{
    /// <summary>Python respondió 200 con un cuerpo válido y status "ok".</summary>
    Online,

    /// <summary>No se pudo establecer conexión (Python apagado, DNS, conexión rechazada, etc.).</summary>
    Unavailable,

    /// <summary>La solicitud excedió el tiempo configurado.</summary>
    Timeout,

    /// <summary>Python respondió pero el cuerpo no es JSON válido o le faltan campos esperados.</summary>
    InvalidResponse,

    /// <summary>Python respondió con un código de error HTTP o un status distinto de "ok".</summary>
    HttpError,
}

public sealed record PythonHealthCheckResult(
    PythonHealthState State,
    string? Service,
    string? Version,
    string? Message);
