namespace Vectorify.Api.Contracts;

/// <summary>Estado reportado por la propia Web API (siempre "online" si esta respuesta se emitió).</summary>
public sealed record ApiHealthInfo(string Status);

/// <summary>
/// Estado del motor Python tal como lo observó la Web API al intentar contactarlo.
/// Status: "online" | "unavailable" | "timeout" | "invalid_response" | "error".
/// </summary>
public sealed record PythonHealthInfo(string Status, string? Service, string? Version, string? Message);

/// <summary>
/// Estado de PostgreSQL tal como lo observó la Web API al chequear la conexión
/// (M2.2-S01). Status: "online" | "unavailable" | "error" -- mismo criterio
/// discriminado que <see cref="PythonHealthInfo"/>.
/// </summary>
public sealed record DatabaseHealthInfo(string Status, string? Message);

/// <summary>
/// Estado global compuesto que consume el frontend React.
/// Status: "online" (todo ok) | "degraded" (API arriba, Python y/o Postgres con problemas).
/// </summary>
public sealed record SystemHealthResponse(
    string Status,
    DateTimeOffset Timestamp,
    ApiHealthInfo Api,
    PythonHealthInfo Python,
    DatabaseHealthInfo Database);
