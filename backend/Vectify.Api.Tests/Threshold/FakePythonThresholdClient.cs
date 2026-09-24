using Vectify.Api.Clients;
using Vectify.Api.Threshold;

namespace Vectify.Api.Tests.Threshold;

/// <summary>IPythonThresholdClient en memoria para tests unitarios de ThresholdService.</summary>
internal sealed class FakePythonThresholdClient : IPythonThresholdClient
{
    private int _callCount;

    public int CallCount => _callCount;
    public Func<ThresholdParameters, PythonThresholdResult>? Respond { get; set; }

    /// <summary>Retraso artificial antes de responder, para forzar solapamiento en tests de concurrencia.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<PythonThresholdResult> ThresholdAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        ThresholdParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return Respond?.Invoke(parameters) ?? DefaultSuccess(parameters);
    }

    private static PythonThresholdResult DefaultSuccess(ThresholdParameters parameters) => new(
        PythonThresholdState.Success,
        [1, 2, 3, 4],
        "image/png",
        10,
        10,
        parameters,
        new ThresholdRawMetrics(40.0, 60.0),
        Message: null);
}
