using System.Globalization;

namespace Vectify.Api.Tests.TestSupport;

/// <summary>Cuerpos JSON crudos (snake_case, como los devuelve Pydantic) para simular al motor Python en tests de vectorización.</summary>
internal static class VectorizePayloads
{
    private const string SimpleSquareSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\">" +
        "<path d=\"M2,2 L8,2 L8,8 L2,8 Z\" fill=\"#000000\"/></svg>";

    public static string SuccessBody(
        string? svg = null,
        int width = 10,
        int height = 10,
        int pathCount = 1,
        int approxNodeCount = 4,
        double minX = 2,
        double minY = 2,
        double maxX = 8,
        double maxY = 8,
        double boundsWidth = 6,
        double boundsHeight = 6,
        string contentType = "image/svg+xml")
    {
        var svgJson = System.Text.Json.JsonSerializer.Serialize(svg ?? SimpleSquareSvg);
        var contentTypeJson = System.Text.Json.JsonSerializer.Serialize(contentType);

        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {
              "svg": {{svgJson}},
              "content_type": {{contentTypeJson}},
              "width": {{width}},
              "height": {{height}},
              "metrics": {
                "path_count": {{pathCount}},
                "approx_node_count": {{approxNodeCount}},
                "bounds": {
                  "min_x": {{minX.ToString(CultureInfo.InvariantCulture)}},
                  "min_y": {{minY.ToString(CultureInfo.InvariantCulture)}},
                  "max_x": {{maxX.ToString(CultureInfo.InvariantCulture)}},
                  "max_y": {{maxY.ToString(CultureInfo.InvariantCulture)}},
                  "width": {{boundsWidth.ToString(CultureInfo.InvariantCulture)}},
                  "height": {{boundsHeight.ToString(CultureInfo.InvariantCulture)}}
                }
              }
            }
            """);
    }

    public static string ErrorBody(string code, string message) =>
        $$"""{"code": "{{code}}", "message": "{{message}}"}""";
}
