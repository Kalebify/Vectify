using System.Globalization;

namespace Vectify.Api.Tests.TestSupport;

/// <summary>Cuerpos JSON crudos (snake_case, como los devuelve Pydantic) para simular al motor Python en tests de simplificación.</summary>
internal static class SimplifyPayloads
{
    private const string SimplifiedSquareSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\">" +
        "<path d=\"M2,2 L8,2 L8,8 L2,8 Z\" fill=\"#000000\"/></svg>";

    public static string SuccessBody(
        string? svg = null,
        double epsilonRatio = 0.004,
        int pathCountBefore = 1,
        int approxNodeCountBefore = 12,
        int pathCountAfter = 1,
        int approxNodeCountAfter = 4,
        double reductionPercent = 66.7,
        double minX = 2,
        double minY = 2,
        double maxX = 8,
        double maxY = 8,
        double boundsWidth = 6,
        double boundsHeight = 6,
        string contentType = "image/svg+xml")
    {
        var svgJson = System.Text.Json.JsonSerializer.Serialize(svg ?? SimplifiedSquareSvg);
        var contentTypeJson = System.Text.Json.JsonSerializer.Serialize(contentType);

        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {
              "svg": {{svgJson}},
              "content_type": {{contentTypeJson}},
              "effective_params": {
                "epsilon_ratio": {{epsilonRatio.ToString(CultureInfo.InvariantCulture)}}
              },
              "metrics": {
                "before": {
                  "path_count": {{pathCountBefore}},
                  "approx_node_count": {{approxNodeCountBefore}},
                  "bounds": {
                    "min_x": {{minX.ToString(CultureInfo.InvariantCulture)}},
                    "min_y": {{minY.ToString(CultureInfo.InvariantCulture)}},
                    "max_x": {{maxX.ToString(CultureInfo.InvariantCulture)}},
                    "max_y": {{maxY.ToString(CultureInfo.InvariantCulture)}},
                    "width": {{boundsWidth.ToString(CultureInfo.InvariantCulture)}},
                    "height": {{boundsHeight.ToString(CultureInfo.InvariantCulture)}}
                  }
                },
                "after": {
                  "path_count": {{pathCountAfter}},
                  "approx_node_count": {{approxNodeCountAfter}},
                  "bounds": {
                    "min_x": {{minX.ToString(CultureInfo.InvariantCulture)}},
                    "min_y": {{minY.ToString(CultureInfo.InvariantCulture)}},
                    "max_x": {{maxX.ToString(CultureInfo.InvariantCulture)}},
                    "max_y": {{maxY.ToString(CultureInfo.InvariantCulture)}},
                    "width": {{boundsWidth.ToString(CultureInfo.InvariantCulture)}},
                    "height": {{boundsHeight.ToString(CultureInfo.InvariantCulture)}}
                  }
                },
                "reduction_percent": {{reductionPercent.ToString(CultureInfo.InvariantCulture)}}
              }
            }
            """);
    }

    public static string ErrorBody(string code, string message) =>
        $$"""{"code": "{{code}}", "message": "{{message}}"}""";
}
