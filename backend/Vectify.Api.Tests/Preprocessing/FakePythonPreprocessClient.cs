using Vectify.Api.Clients;
using Vectify.Api.Preprocessing;

namespace Vectify.Api.Tests.Preprocessing;

/// <summary>IPythonPreprocessClient en memoria para tests unitarios de PreprocessService.</summary>
internal sealed class FakePythonPreprocessClient : IPythonPreprocessClient
{
    private int _callCount;

    public int CallCount => _callCount;
    public Func<PreprocessParameters, PythonPreprocessResult>? Respond { get; set; }

    /// <summary>
    /// Retraso artificial antes de responder, para forzar que dos llamadas
    /// concurrentes se solapen en tests de la sección crítica de caché/lock de
    /// PreprocessService (por defecto, ninguno: comportamiento síncrono de
    /// siempre).
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<PythonPreprocessResult> PreprocessAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        PreprocessParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return Respond?.Invoke(parameters) ?? DefaultSuccess(parameters);
    }

    private static PythonPreprocessResult DefaultSuccess(PreprocessParameters parameters) => new(
        PythonPreprocessState.Success,
        [1, 2, 3, 4],
        "image/png",
        10,
        10,
        10,
        10,
        parameters,
        new PreprocessMetrics(128.0, 10.0, 0, 255),
        Message: null);
}
