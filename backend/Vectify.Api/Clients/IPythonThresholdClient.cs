using Vectify.Api.Threshold;

namespace Vectify.Api.Clients;

/// <summary>Cliente tipado hacia el endpoint de threshold del motor Python/FastAPI.</summary>
public interface IPythonThresholdClient
{
    /// <summary>
    /// Envía el preview YA preprocesado (M1-S03, nunca el original crudo) y
    /// los parámetros de threshold ya validados. Nunca lanza excepciones:
    /// cualquier falla (offline, timeout, imagen corrupta, dimensiones
    /// excesivas, respuesta inválida, error HTTP) se traduce a un
    /// <see cref="PythonThresholdResult"/> con el estado correspondiente.
    /// </summary>
    Task<PythonThresholdResult> ThresholdAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        ThresholdParameters parameters,
        CancellationToken cancellationToken = default);
}
