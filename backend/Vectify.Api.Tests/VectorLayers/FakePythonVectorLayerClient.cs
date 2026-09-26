using Vectify.Api.Clients;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.VectorLayers;

/// <summary>IPythonVectorLayerClient en memoria para tests unitarios de VectorLayerService.</summary>
internal sealed class FakePythonVectorLayerClient : IPythonVectorLayerClient
{
    private int _callCount;

    public int CallCount => _callCount;

    /// <summary>Si se define, sustituye la respuesta default; recibe las máscaras enviadas para poder inspeccionarlas/responder en función de ellas.</summary>
    public Func<IReadOnlyList<(Guid GroupId, Stream Content, string ContentType)>, PythonVectorLayerBatchResult>? Respond { get; set; }

    /// <summary>Retraso artificial antes de responder, para forzar solapamiento en tests de concurrencia.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<PythonVectorLayerBatchResult> VectorizeLayersAsync(
        IReadOnlyList<(Guid GroupId, Stream Content, string ContentType)> masks,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return Respond?.Invoke(masks) ?? DefaultSuccess(masks);
    }

    /// <summary>Una capa por cada máscara recibida, con el mismo GroupId -- mismo criterio "eco" que el motor Python real.</summary>
    public static PythonVectorLayerBatchResult DefaultSuccess(
        IReadOnlyList<(Guid GroupId, Stream Content, string ContentType)> masks) => new(
        PythonVectorLayerState.Success,
        masks.Select(mask => new PythonVectorLayerItemResult(
            mask.GroupId,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M2,2 L8,2 L8,8 L2,8 Z\"/></svg>",
            "image/svg+xml",
            10,
            10,
            new VectorMetrics(1, 4, new VectorBounds(2, 2, 8, 8, 6, 6)))).ToList(),
        Message: null);
}
