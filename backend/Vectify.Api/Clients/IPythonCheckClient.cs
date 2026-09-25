using Vectify.Api.Checking;

namespace Vectify.Api.Clients;

/// <summary>
/// Cliente tipado hacia el endpoint del Laser Checker de paths abiertos/
/// duplicados (POST /api/v1/check) del motor Python/FastAPI. Cada etapa del
/// pipeline tiene su propio cliente con su propio timeout (mismo patrón que
/// IPythonSimplifyClient/IPythonVectorizeClient).
/// </summary>
public interface IPythonCheckClient
{
    /// <summary>
    /// Envía el SVG YA generado (una VectorVersion o SimplificationVersion
    /// existente) y las tolerancias efectivas. Nunca lanza excepciones:
    /// cualquier falla (offline, timeout, SVG de entrada inválido/demasiado
    /// grande/con demasiados subpaths, respuesta inválida, error HTTP) se
    /// traduce a un <see cref="PythonCheckResult"/> con el estado
    /// correspondiente.
    /// </summary>
    Task<PythonCheckResult> CheckAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        CheckParameters parameters,
        CancellationToken cancellationToken = default);
}
