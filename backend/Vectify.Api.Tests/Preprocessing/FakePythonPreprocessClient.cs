using Vectify.Api.Clients;
using Vectify.Api.Preprocessing;

namespace Vectify.Api.Tests.Preprocessing;

/// <summary>IPythonPreprocessClient en memoria para tests unitarios de PreprocessService.</summary>
internal sealed class FakePythonPreprocessClient : IPythonPreprocessClient
{
    public int CallCount { get; private set; }
    public Func<PreprocessParameters, PythonPreprocessResult>? Respond { get; set; }

    public Task<PythonPreprocessResult> PreprocessAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        PreprocessParameters parameters,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        var result = Respond?.Invoke(parameters) ?? DefaultSuccess(parameters);
        return Task.FromResult(result);
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
