using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda (snake_case, tal como la serializa Pydantic) de la respuesta de
/// POST /api/v1/preprocess del motor Python/FastAPI.
/// </summary>
public sealed class PythonPreprocessPayload
{
    [JsonPropertyName("image_base64")]
    public string? ImageBase64 { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("original_width")]
    public int OriginalWidth { get; set; }

    [JsonPropertyName("original_height")]
    public int OriginalHeight { get; set; }

    [JsonPropertyName("effective_params")]
    public PythonPreprocessParamsPayload? EffectiveParams { get; set; }

    [JsonPropertyName("metrics")]
    public PythonPreprocessMetricsPayload? Metrics { get; set; }
}

public sealed class PythonPreprocessParamsPayload
{
    [JsonPropertyName("grayscale")]
    public bool Grayscale { get; set; }

    [JsonPropertyName("contrast")]
    public double Contrast { get; set; }

    [JsonPropertyName("brightness")]
    public int Brightness { get; set; }

    [JsonPropertyName("denoise")]
    public int Denoise { get; set; }
}

public sealed class PythonPreprocessMetricsPayload
{
    [JsonPropertyName("mean_brightness")]
    public double MeanBrightness { get; set; }

    [JsonPropertyName("std_dev")]
    public double StdDev { get; set; }

    [JsonPropertyName("min_value")]
    public int MinValue { get; set; }

    [JsonPropertyName("max_value")]
    public int MaxValue { get; set; }
}

/// <summary>Forma cruda del error controlado que devuelve el motor Python: { "code", "message" }.</summary>
public sealed class PythonErrorPayload
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
