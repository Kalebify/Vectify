namespace Vectify.Api.Contracts;

/// <summary>
/// Respuesta de POST/GET de la vectorización. Ver spec.md M1-S05:
/// "persistir una VectorVersion y devolver metadatos" + "devolver
/// estadísticas como paths/nodos aproximados/bounds".
/// </summary>
public sealed record VectorizeResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid VectorId,
    string SvgUrl,
    Guid SourceMaskId,
    int Version,
    int Width,
    int Height,
    VectorMetricsPayload Metrics,
    bool Cached);

public sealed record VectorMetricsPayload(int PathCount, int ApproxNodeCount, VectorBoundsPayload Bounds);

public sealed record VectorBoundsPayload(double MinX, double MinY, double MaxX, double MaxY, double Width, double Height);
