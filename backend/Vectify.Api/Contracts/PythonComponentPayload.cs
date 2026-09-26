using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda (snake_case, tal como la serializa Pydantic) de la respuesta
/// de POST /api/v1/components del motor Python/FastAPI -- mismo criterio que
/// <see cref="PythonCheckPayload"/>.
/// </summary>
public sealed class PythonComponentPayload
{
    [JsonPropertyName("effective_params")]
    public PythonComponentParamsPayload? EffectiveParams { get; set; }

    [JsonPropertyName("summary")]
    public PythonComponentSummaryPayload? Summary { get; set; }

    [JsonPropertyName("components")]
    public List<PythonComponentItemPayload>? Components { get; set; }

    [JsonPropertyName("skipped_path_count")]
    public int SkippedPathCount { get; set; }
}

public sealed class PythonComponentParamsPayload
{
    [JsonPropertyName("touch_ratio")]
    public double TouchRatio { get; set; }

    [JsonPropertyName("tiny_area_ratio")]
    public double TinyAreaRatio { get; set; }
}

public sealed class PythonComponentSummaryPayload
{
    [JsonPropertyName("component_count")]
    public int ComponentCount { get; set; }

    [JsonPropertyName("tiny_component_count")]
    public int TinyComponentCount { get; set; }
}

public sealed class PythonComponentBoundsPayload
{
    [JsonPropertyName("min_x")]
    public double MinX { get; set; }

    [JsonPropertyName("min_y")]
    public double MinY { get; set; }

    [JsonPropertyName("max_x")]
    public double MaxX { get; set; }

    [JsonPropertyName("max_y")]
    public double MaxY { get; set; }
}

public sealed class PythonComponentMemberPayload
{
    [JsonPropertyName("path_index")]
    public int PathIndex { get; set; }

    [JsonPropertyName("subpath_index")]
    public int SubpathIndex { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("bounds")]
    public PythonComponentBoundsPayload? Bounds { get; set; }

    [JsonPropertyName("area")]
    public double? Area { get; set; }
}

public sealed class PythonComponentItemPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("members")]
    public List<PythonComponentMemberPayload>? Members { get; set; }

    [JsonPropertyName("bounds")]
    public PythonComponentBoundsPayload? Bounds { get; set; }

    [JsonPropertyName("area")]
    public double? Area { get; set; }

    [JsonPropertyName("is_tiny")]
    public bool? IsTiny { get; set; }
}
