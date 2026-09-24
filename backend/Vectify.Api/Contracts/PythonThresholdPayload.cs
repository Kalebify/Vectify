using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda (snake_case, tal como la serializa Pydantic) de la respuesta de
/// POST /api/v1/threshold del motor Python/FastAPI.
/// </summary>
public sealed class PythonThresholdPayload
{
    [JsonPropertyName("image_base64")]
    public string? ImageBase64 { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("effective_params")]
    public PythonThresholdParamsPayload? EffectiveParams { get; set; }

    [JsonPropertyName("metrics")]
    public PythonThresholdMetricsPayload? Metrics { get; set; }
}

public sealed class PythonThresholdParamsPayload
{
    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("invert")]
    public bool Invert { get; set; }
}

public sealed class PythonThresholdMetricsPayload
{
    [JsonPropertyName("foreground_percent")]
    public double ForegroundPercent { get; set; }

    [JsonPropertyName("background_percent")]
    public double BackgroundPercent { get; set; }
}
