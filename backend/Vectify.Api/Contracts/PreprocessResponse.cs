namespace Vectify.Api.Contracts;

/// <summary>
/// Respuesta de POST/GET del preview de preprocesamiento. Ver spec.md M1-S03,
/// sección "Contratos": "Salida: previewId/URL, dimensiones, parámetros
/// efectivos y métricas básicas".
/// </summary>
public sealed record PreprocessResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid PreviewId,
    string PreviewUrl,
    int Version,
    int Width,
    int Height,
    int OriginalWidth,
    int OriginalHeight,
    PreprocessParametersPayload EffectiveParams,
    PreprocessMetricsPayload Metrics,
    bool Cached);

public sealed record PreprocessParametersPayload(bool Grayscale, double Contrast, int Brightness, int Denoise);

public sealed record PreprocessMetricsPayload(double MeanBrightness, double StdDev, int MinValue, int MaxValue);
