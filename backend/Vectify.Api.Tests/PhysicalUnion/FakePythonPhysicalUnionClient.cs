using Vectify.Api.Clients;
using Vectify.Api.Components;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.PhysicalUnion;

/// <summary>IPythonPhysicalUnionClient en memoria para tests unitarios de PhysicalUnionService. Mismo criterio que Tests.Components.FakePythonComponentClient.</summary>
internal sealed class FakePythonPhysicalUnionClient : IPythonPhysicalUnionClient
{
    private int _callCount;

    public int CallCount => _callCount;
    public IReadOnlyList<LayerComponent>? LastSelectedComponents { get; private set; }

    /// <summary>Si se define, sustituye la respuesta default.</summary>
    public Func<PythonPhysicalUnionResult>? Respond { get; set; }

    public Task<PythonPhysicalUnionResult> UnionAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        IReadOnlyList<LayerComponent> selectedComponents,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        LastSelectedComponents = selectedComponents;
        return Task.FromResult(Respond?.Invoke() ?? DefaultSuccess());
    }

    public static PythonPhysicalUnionResult DefaultSuccess() => new(
        PythonPhysicalUnionState.Success,
        Svg: "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\"><path d=\"M0,0 L10,0 L10,10 L0,10 Z\" fill=\"#000000\"/></svg>",
        ContentType: "image/svg+xml",
        Width: 100,
        Height: 100,
        Metrics: new VectorMetrics(1, 4, new VectorBounds(0, 0, 10, 10, 10, 10)),
        ComponentCountBefore: 2,
        ComponentCountAfter: 1,
        Strategy: "bridge",
        BridgeCount: 1,
        Message: null);
}
