using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda (snake_case, tal como la serializa Pydantic) de la respuesta
/// de POST /api/v1/vectorize-layers del motor Python/FastAPI (M2-S02).
/// Reutiliza <see cref="PythonVectorMetricsPayload"/> (ya definido para
/// POST /api/v1/vectorize): cada capa tiene exactamente la misma forma de
/// métricas que una vectorización individual.
/// </summary>
public sealed class PythonVectorizeLayersPayload
{
    [JsonPropertyName("layers")]
    public List<PythonVectorLayerItemPayload>? Layers { get; set; }
}

public sealed class PythonVectorLayerItemPayload
{
    [JsonPropertyName("group_id")]
    public string? GroupId { get; set; }

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
