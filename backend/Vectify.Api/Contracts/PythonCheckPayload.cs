using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda (snake_case, tal como la serializa Pydantic) de la respuesta
/// de POST /api/v1/check del motor Python/FastAPI. `PythonCheckIssuePayload`
/// está deliberadamente "aplanado" (todos los campos de ambos tipos de issue,
/// nullable) en vez de un tipo por cada variante -- System.Text.Json no
/// deserializa polimorfismo por discriminador sin configuración adicional
/// del lado de INPUT (a diferencia de la serialización de salida hacia React
/// en <see cref="CheckIssuePayload"/>, que sí usa [JsonPolymorphic]); acá
/// alcanza con leer el campo `type` para decidir a qué tipo de dominio
/// convertir cada elemento -- ver PythonCheckClient.
/// </summary>
public sealed class PythonCheckPayload
{
    [JsonPropertyName("effective_params")]
    public PythonCheckParamsPayload? EffectiveParams { get; set; }

    [JsonPropertyName("summary")]
    public PythonCheckSummaryPayload? Summary { get; set; }

    [JsonPropertyName("issues")]
    public List<PythonCheckIssuePayload>? Issues { get; set; }

    [JsonPropertyName("skipped_path_count")]
    public int SkippedPathCount { get; set; }
}

public sealed class PythonCheckParamsPayload
{
    [JsonPropertyName("close_gap_ratio")]
    public double CloseGapRatio { get; set; }

    [JsonPropertyName("duplicate_point_ratio")]
    public double DuplicatePointRatio { get; set; }
}

public sealed class PythonCheckSummaryPayload
{
    [JsonPropertyName("open_path_count")]
    public int OpenPathCount { get; set; }

    [JsonPropertyName("duplicate_group_count")]
    public int DuplicateGroupCount { get; set; }
}

public sealed class PythonCheckBoundsPayload
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

public sealed class PythonCheckMemberPayload
{
    [JsonPropertyName("path_index")]
    public int PathIndex { get; set; }

    [JsonPropertyName("subpath_index")]
    public int SubpathIndex { get; set; }

    [JsonPropertyName("bounds")]
    public PythonCheckBoundsPayload? Bounds { get; set; }
}

public sealed class PythonCheckIssuePayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    // Campos de "open_path".
    [JsonPropertyName("path_index")]
    public int? PathIndex { get; set; }

    [JsonPropertyName("subpath_index")]
    public int? SubpathIndex { get; set; }

    [JsonPropertyName("start_point")]
    public List<double>? StartPoint { get; set; }

    [JsonPropertyName("end_point")]
    public List<double>? EndPoint { get; set; }

    [JsonPropertyName("gap_distance")]
    public double? GapDistance { get; set; }

    [JsonPropertyName("bounds")]
    public PythonCheckBoundsPayload? Bounds { get; set; }

    // Campos de "duplicate_path".
    [JsonPropertyName("exact")]
    public bool? Exact { get; set; }

    [JsonPropertyName("max_point_distance")]
    public double? MaxPointDistance { get; set; }

    [JsonPropertyName("members")]
    public List<PythonCheckMemberPayload>? Members { get; set; }
}
