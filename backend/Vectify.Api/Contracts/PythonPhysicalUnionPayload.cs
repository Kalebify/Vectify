using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda (snake_case, tal como la serializa Pydantic) de la respuesta
/// de POST /api/v1/components/union del motor Python/FastAPI -- mismo
/// criterio que <see cref="PythonComponentPayload"/>.
/// </summary>
public sealed class PythonPhysicalUnionPayload
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
    public PythonPhysicalUnionMetricsPayload? Metrics { get; set; }

    [JsonPropertyName("component_count_before")]
    public int ComponentCountBefore { get; set; }

    [JsonPropertyName("component_count_after")]
    public int ComponentCountAfter { get; set; }

    [JsonPropertyName("expected_component_count_after")]
    public int ExpectedComponentCountAfter { get; set; }

    [JsonPropertyName("strategy")]
    public string? Strategy { get; set; }

    [JsonPropertyName("bridge_count")]
    public int BridgeCount { get; set; }
}

public sealed class PythonPhysicalUnionMetricsPayload
{
    [JsonPropertyName("path_count")]
    public int PathCount { get; set; }

    [JsonPropertyName("approx_node_count")]
    public int ApproxNodeCount { get; set; }

    [JsonPropertyName("bounds")]
    public PythonPhysicalUnionBoundsPayload? Bounds { get; set; }
}

public sealed class PythonPhysicalUnionBoundsPayload
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

/// <summary>Cuerpo JSON (snake_case) enviado a Python en el campo multipart 'params' -- ver PhysicalUnionParams del lado Python.</summary>
public sealed class PythonPhysicalUnionRequestPayload
{
    [JsonPropertyName("selections")]
    public List<PythonPhysicalUnionSelectionPayload> Selections { get; set; } = new();
}

public sealed class PythonPhysicalUnionSelectionPayload
{
    [JsonPropertyName("component_id")]
    public string ComponentId { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public List<PythonPhysicalUnionMemberPayload> Members { get; set; } = new();
}

public sealed class PythonPhysicalUnionMemberPayload
{
    [JsonPropertyName("path_index")]
    public int PathIndex { get; set; }

    [JsonPropertyName("subpath_index")]
    public int SubpathIndex { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}
