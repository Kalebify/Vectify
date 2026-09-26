namespace Vectorify.Api.Vectorization;

/// <summary>
/// Estadísticas del SVG resultante, calculadas por el motor Python (ver
/// spec.md M1-S05: "Python/FastAPI: ... devolver estadísticas como paths/
/// nodos aproximados/bounds"). A diferencia de ThresholdMetrics, no hay
/// clasificación adicional de Vectorify.Api sobre estos valores -- no existe una
/// regla de negocio equivalente a "casi vacía/casi llena" para esta etapa.
/// </summary>
public sealed record VectorMetrics(int PathCount, int ApproxNodeCount, VectorBounds Bounds);
