using Vectify.Api.Simplification;

namespace Vectify.Api.Clients;

/// <summary>
/// Cliente tipado hacia el endpoint de simplificación de nodos (POST
/// /api/v1/simplify) del motor Python/FastAPI. Cada etapa del pipeline tiene
/// su propio cliente con su propio timeout (mismo patrón que
/// IPythonVectorizeClient/IPythonThresholdClient).
/// </summary>
public interface IPythonSimplifyClient
{
    /// <summary>
    /// Envía el SVG YA generado (una VectorVersion existente, M1-S05) y los
    /// parámetros efectivos (epsilon ya resuelto, nunca el nombre del
    /// preset). Nunca lanza excepciones: cualquier falla (offline, timeout,
    /// SVG de entrada inválido/demasiado grande, respuesta inválida, error
    /// HTTP) se traduce a un <see cref="PythonSimplifyResult"/> con el estado
    /// correspondiente.
    /// </summary>
    Task<PythonSimplifyResult> SimplifyAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        SimplificationParameters parameters,
        CancellationToken cancellationToken = default);
}
