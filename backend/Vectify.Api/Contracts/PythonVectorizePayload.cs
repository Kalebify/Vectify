using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda (snake_case, tal como la serializa Pydantic) de la respuesta de
/// POST /api/v1/vectorize del motor Python/FastAPI.
/// </summary>
public sealed class PythonVectorizePayload
{
    [JsonPropertyName("svg")]
    public string? Svg { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("metrics")]
    public PythonVectorMetricsPayload? Metrics { get; set; }
}

public sealed class PythonVectorMetricsPayload
{
    [JsonPropertyName("path_count")]
    public int PathCount { get; set; }

    [JsonPropertyName("approx_node_count")]
    public int ApproxNodeCount { get; set; }

    [JsonPropertyName("bounds")]
    public PythonVectorBoundsPayload? Bounds { get; set; }
}

public sealed class PythonVectorBoundsPayload
{
    [JsonPropertyName("min_x")]
    public double MinX { get; set; }

    [JsonPropertyName("min_y")]
    public double MinY { get; set; }

    [JsonPropertyName("max_x")]
    public double MaxX { get; set; }

    [JsonPropertyName("max_y")]
    public double MaxY { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}
