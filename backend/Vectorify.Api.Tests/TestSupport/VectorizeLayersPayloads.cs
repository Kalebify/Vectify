using System.Globalization;

namespace Vectorify.Api.Tests.TestSupport;

/// <summary>Cuerpos JSON crudos (snake_case, como los devuelve Pydantic) para simular al motor Python en tests de POST /api/v1/vectorize-layers (M2-S02/M2.1-S03).</summary>
internal static class VectorizeLayersPayloads
{
    private const string SimpleSquareSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\">" +
        "<path d=\"M0,0 L1,0 L1,1 L0,1 Z\" fill=\"#000000\"/></svg>";

    public static string DefaultRasterValidationJson(
        double ownMismatchRatio = 0.0,
        double ownMismatchTolerance = 0.15,
        bool ownMismatchWithinTolerance = true,
        double contaminationRatio = 0.0,
        double contaminationTolerance = 0.01,
        bool contaminationWithinTolerance = true) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        {
          "own_mismatch_ratio": {{ownMismatchRatio}},
          "own_mismatch_tolerance": {{ownMismatchTolerance}},
          "own_mismatch_within_tolerance": {{(ownMismatchWithinTolerance ? "true" : "false")}},
          "contamination_ratio": {{contaminationRatio}},
          "contamination_tolerance": {{contaminationTolerance}},
          "contamination_within_tolerance": {{(contaminationWithinTolerance ? "true" : "false")}},
          "warnings": []
        }
        """);

    public static string LayerJson(string groupId, string? svg = null, int width = 1, int height = 1, string? rasterValidationJson = null)
    {
        var svgJson = System.Text.Json.JsonSerializer.Serialize(svg ?? SimpleSquareSvg);
        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {
              "group_id": "{{groupId}}",
              "svg": {{svgJson}},
              "content_type": "image/svg+xml",
              "width": {{width}},
              "height": {{height}},
              "metrics": {"path_count": 1, "approx_node_count": 4, "bounds": {"min_x": 0.0, "min_y": 0.0, "max_x": 1.0, "max_y": 1.0, "width": 1.0, "height": 1.0} },
              "raster_validation": {{rasterValidationJson ?? DefaultRasterValidationJson()}}
            }
            """);
    }

    /// <summary>Un conjunto de capas con UN solo group_id -- el caller debe pasar el mismo GroupId (formato "N") que envió .NET, ver PythonVectorLayerClient.</summary>
    public static string SuccessBody(string groupId, int width = 1, int height = 1) =>
        $$"""{"layers": [{{LayerJson(groupId, width: width, height: height)}}]}""";

    public static string ErrorBody(string code, string message) =>
        $$"""{"code": "{{code}}", "message": "{{message}}"}""";
}
