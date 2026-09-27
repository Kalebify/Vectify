namespace Vectorify.Api.Tests.TestSupport;

/// <summary>Cuerpos JSON crudos (snake_case, como los devuelve Pydantic) para simular al motor Python en tests de paleta de colores.</summary>
internal static class ColorPalettePayloads
{
    /// <summary>Base64 de un PNG 1x1 válido (mismos bytes que SampleImages.ValidPng1x1).</summary>
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    public static string SuccessBody(
        int width = 1,
        int height = 1,
        double tolerance = 12.0,
        int? maxColors = null,
        double transparentPercent = 0.0,
        bool touchesBorder = false)
    {
        var maxColorsJson = maxColors.HasValue ? maxColors.Value.ToString() : "null";
        var touchesBorderJson = touchesBorder ? "true" : "false";

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $$"""
            {
              "width": {{width}},
              "height": {{height}},
              "content_type": "image/png",
              "effective_params": {
                "tolerance": {{tolerance}},
                "max_colors": {{maxColorsJson}},
                "tiny_area_ratio": 0.001
              },
              "metrics": {
                "color_count": 1,
                "transparent_percent": {{transparentPercent}}
              },
              "groups": [
                {
                  "id": 0,
                  "color_hex": "#3a6ea5",
                  "pixel_count": 4,
                  "area_percent": 100.0,
                  "has_partial_alpha": false,
                  "touches_border": {{touchesBorderJson}},
                  "mask_base64": "{{TinyPngBase64}}"
                }
              ],
              "quantized_preview_base64": "{{TinyPngBase64}}"
            }
            """);
    }

    public static string ErrorBody(string code, string message) =>
        $$"""{"code": "{{code}}", "message": "{{message}}"}""";
}
