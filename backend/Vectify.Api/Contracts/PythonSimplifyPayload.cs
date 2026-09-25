using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda (snake_case, tal como la serializa Pydantic) de la respuesta de
/// POST /api/v1/simplify del motor Python/FastAPI. Reutiliza
/// <see cref="PythonVectorMetricsPayload"/>/<see cref="PythonVectorBoundsPayload"/>
/// (ya definidos para PythonVectorizePayload) para "before"/"after": son la
/// misma forma de estadística (path_count/approx_node_count/bounds).
/// </summary>
public sealed class PythonSimplifyPayload
{
    [JsonPropertyName("svg")]
    public string? Svg { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("effective_params")]
    public PythonSimplifyParamsPayload? EffectiveParams { get; set; }

    [JsonPropertyName("metrics")]
    public PythonSimplifyMetricsPayload? Metrics { get; set; }
}

public sealed class PythonSimplifyParamsPayload
{
    [JsonPropertyName("epsilon_ratio")]
    public double EpsilonRatio { get; set; }
}

public sealed class PythonSimplifyMetricsPayload
{
    [JsonPropertyName("before")]
    public PythonVectorMetricsPayload? Before { get; set; }

    [JsonPropertyName("after")]
    public PythonVectorMetricsPayload? After { get; set; }

    [JsonPropertyName("reduction_percent")]
    public double ReductionPercent { get; set; }
}
