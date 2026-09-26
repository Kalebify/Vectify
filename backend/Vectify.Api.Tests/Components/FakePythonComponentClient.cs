using Vectify.Api.Clients;
using Vectify.Api.Components;

namespace Vectify.Api.Tests.Components;

/// <summary>IPythonComponentClient en memoria para tests unitarios de ComponentAnalysisService. Mismo criterio que VectorLayers.Tests.FakePythonVectorLayerClient.</summary>
internal sealed class FakePythonComponentClient : IPythonComponentClient
{
    private int _callCount;

    public int CallCount => _callCount;

    /// <summary>Si se define, sustituye la respuesta default.</summary>
    public Func<PythonComponentResult>? Respond { get; set; }

    /// <summary>Retraso artificial antes de responder, para forzar solapamiento en tests de concurrencia.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<PythonComponentResult> AnalyzeAsync(
        Stream svgContent, string contentType, string fileName, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return Respond?.Invoke() ?? DefaultSuccess();
    }

    public static PythonComponentResult DefaultSuccess() => new(
        PythonComponentState.Success,
        new List<LayerComponent>
        {
            new(
                "component-1",
                new List<ComponentMember> { new(0, 0, "solid", new ComponentBounds(2, 2, 8, 8), 36) },
                new ComponentBounds(2, 2, 8, 8),
                36,
                false),
        },
        SkippedPathCount: 0,
        Message: null);
}
