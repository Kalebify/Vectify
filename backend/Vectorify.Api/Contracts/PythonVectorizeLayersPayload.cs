using System.Text.Json.Serialization;

namespace Vectorify.Api.Contracts;

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

    [JsonPropertyName("raster_validation")]
    public PythonRasterValidationPayload? RasterValidation { get; set; }
}

/// <summary>
/// Forma cruda (snake_case) de `RasterValidationResult` (M2.1-S03, Python) --
/// ver Vectorify.Api.VectorLayers.LayerRasterValidation, la forma tipada
/// ya validada a la que se convierte.
/// </summary>
public sealed class PythonRasterValidationPayload
{
    [JsonPropertyName("own_mismatch_ratio")]
    public double OwnMismatchRatio { get; set; }

    [JsonPropertyName("own_mismatch_tolerance")]
    public double OwnMismatchTolerance { get; set; }

    [JsonPropertyName("own_mismatch_within_tolerance")]
    public bool OwnMismatchWithinTolerance { get; set; }

    [JsonPropertyName("contamination_ratio")]
    public double ContaminationRatio { get; set; }

    [JsonPropertyName("contamination_tolerance")]
    public double ContaminationTolerance { get; set; }

    [JsonPropertyName("contamination_within_tolerance")]
    public bool ContaminationWithinTolerance { get; set; }

    [JsonPropertyName("warnings")]
    public List<string>? Warnings { get; set; }
}
