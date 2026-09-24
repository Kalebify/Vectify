namespace Vectify.Api.Clients;

/// <summary>
/// Cliente tipado hacia el motor de vectorización Python/FastAPI.
/// En esta fase solo expone el chequeo de salud; los métodos de vectorización
/// se agregarán en sprints posteriores.
/// </summary>
public interface IPythonVectorizationClient
{
    /// <summary>
    /// Consulta GET /health del motor Python. Nunca lanza excepciones: cualquier
    /// falla (offline, timeout, respuesta inválida, error HTTP) se traduce a un
    /// <see cref="PythonHealthCheckResult"/> con el estado correspondiente.
    /// </summary>
    Task<PythonHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}
