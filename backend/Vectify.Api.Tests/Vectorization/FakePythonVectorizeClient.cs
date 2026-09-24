using Vectify.Api.Clients;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Vectorization;

/// <summary>IPythonVectorizeClient en memoria para tests unitarios de VectorizationService.</summary>
internal sealed class FakePythonVectorizeClient : IPythonVectorizeClient
{
    private int _callCount;

    public int CallCount => _callCount;
    public Func<PythonVectorizeResult>? Respond { get; set; }

    /// <summary>Retraso artificial antes de responder, para forzar solapamiento en tests de concurrencia.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<PythonVectorizeResult> VectorizeAsync(
        Stream maskContent,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return Respond?.Invoke() ?? DefaultSuccess();
    }

    private static PythonVectorizeResult DefaultSuccess() => new(
        PythonVectorizeState.Success,
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M2,2 L8,2 L8,8 L2,8 Z\"/></svg>",
        "image/svg+xml",
        10,
        10,
        new VectorMetrics(1, 4, new VectorBounds(2, 2, 8, 8, 6, 6)),
        Message: null);
}
