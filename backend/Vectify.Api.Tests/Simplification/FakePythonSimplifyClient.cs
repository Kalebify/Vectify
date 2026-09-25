using Vectify.Api.Clients;
using Vectify.Api.Simplification;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Simplification;

/// <summary>IPythonSimplifyClient en memoria para tests unitarios de SimplificationService.</summary>
internal sealed class FakePythonSimplifyClient : IPythonSimplifyClient
{
    private int _callCount;

    public int CallCount => _callCount;
    public Func<PythonSimplifyResult>? Respond { get; set; }

    /// <summary>Retraso artificial antes de responder, para forzar solapamiento en tests de concurrencia.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<PythonSimplifyResult> SimplifyAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        SimplificationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return Respond?.Invoke() ?? DefaultSuccess();
    }

    private static PythonSimplifyResult DefaultSuccess() => new(
        PythonSimplifyState.Success,
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M2,2 L8,2 L8,8 L2,8 Z\"/></svg>",
        "image/svg+xml",
        new SimplificationMetrics(
            new VectorMetrics(1, 12, new VectorBounds(2, 2, 8, 8, 6, 6)),
            new VectorMetrics(1, 4, new VectorBounds(2, 2, 8, 8, 6, 6)),
            ReductionPercent: 66.7),
        Message: null);
}
