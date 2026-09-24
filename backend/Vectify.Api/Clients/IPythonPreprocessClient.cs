using Vectify.Api.Preprocessing;

namespace Vectify.Api.Clients;

/// <summary>Cliente tipado hacia el endpoint de preprocesamiento del motor Python/FastAPI.</summary>
public interface IPythonPreprocessClient
{
    /// <summary>
    /// Envía la imagen original (nunca se modifica en la Web API: se reenvía tal
    /// cual se guardó) y los parámetros ya validados. Nunca lanza excepciones:
    /// cualquier falla (offline, timeout, imagen corrupta, dimensiones excesivas,
    /// respuesta inválida, error HTTP) se traduce a un <see cref="PythonPreprocessResult"/>
    /// con el estado correspondiente.
    /// </summary>
    Task<PythonPreprocessResult> PreprocessAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        PreprocessParameters parameters,
        CancellationToken cancellationToken = default);
}
