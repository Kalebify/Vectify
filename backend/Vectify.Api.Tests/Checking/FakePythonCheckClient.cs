using Vectify.Api.Checking;
using Vectify.Api.Clients;

namespace Vectify.Api.Tests.Checking;

/// <summary>IPythonCheckClient en memoria para tests unitarios de CheckService.</summary>
internal sealed class FakePythonCheckClient : IPythonCheckClient
{
    private int _callCount;

    public int CallCount => _callCount;
    public Func<PythonCheckResult>? Respond { get; set; }

    public Task<PythonCheckResult> CheckAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        CheckParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(Respond?.Invoke() ?? DefaultSuccess());
    }

    private static PythonCheckResult DefaultSuccess() => new(
        PythonCheckState.Success,
        new List<CheckIssue>
        {
            new CheckIssue.OpenPath(
                "open-0-0", "error", 0, 0, (0, 0), (0.02, 0.01), 0.022,
                new CheckBounds(0, 0, 10, 10)),
        },
        SkippedPathCount: 0,
        Message: null);
}
