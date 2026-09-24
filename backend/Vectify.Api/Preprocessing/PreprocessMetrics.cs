namespace Vectify.Api.Preprocessing;

/// <summary>Métricas básicas del preview generado, calculadas por el motor Python.</summary>
public sealed record PreprocessMetrics(double MeanBrightness, double StdDev, int MinValue, int MaxValue);
