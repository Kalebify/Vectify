using Vectify.Api.Vectorization;

namespace Vectify.Api.Clients;

/// <summary>
/// Cliente tipado hacia el endpoint de vectorización (POST /api/v1/vectorize)
/// del motor Python/FastAPI. Deliberadamente distinto de
/// <see cref="IPythonVectorizationClient"/> (que, pese a su nombre, hoy solo
/// expone el chequeo de salud consumido por /api/v1/system/health): cada
/// etapa del pipeline tiene su propio cliente con su propio timeout (mismo
/// patrón que IPythonPreprocessClient/IPythonThresholdClient), para no atar
/// el timeout de un trazado potencialmente lento al timeout corto del
/// chequeo de salud.
/// </summary>
public interface IPythonVectorizeClient
{
    /// <summary>
    /// Envía la máscara B/N YA generada por threshold (M1-S04, nunca el
    /// original ni el preview preprocesado). Nunca lanza excepciones:
    /// cualquier falla (offline, timeout, máscara corrupta/vacía, SVG
    /// demasiado grande, respuesta inválida, error HTTP) se traduce a un
    /// <see cref="PythonVectorizeResult"/> con el estado correspondiente.
    /// </summary>
    Task<PythonVectorizeResult> VectorizeAsync(
        Stream maskContent,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default);
}
