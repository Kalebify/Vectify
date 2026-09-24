using System.Globalization;

namespace Vectify.Api.Tests.TestSupport;

/// <summary>Cuerpos JSON crudos (snake_case, como los devuelve Pydantic) para simular al motor Python en tests.</summary>
internal static class PreprocessPayloads
{
    /// <summary>Base64 de un PNG 1x1 válido (mismos bytes que SampleImages.ValidPng1x1).</summary>
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    public static string SuccessBody(
        bool grayscale = false,
        double contrast = 1.0,
        int brightness = 0,
        int denoise = 0,
        int width = 1,
        int height = 1,
        int originalWidth = 1,
        int originalHeight = 1,
        double meanBrightness = 128.0,
        double stdDev = 10.0,
        int minValue = 0,
        int maxValue = 255)
    {
        // string.Create(CultureInfo.InvariantCulture, ...) es imprescindible acá:
        // sin especificar cultura, los doubles (contrast, meanBrightness, stdDev) se
        // formatean con la cultura actual del proceso (p. ej. "1,4" en es-*), lo que
        // rompe el JSON. El motor Python real nunca tiene este problema porque
        // System.Text.Json siempre serializa con invariant culture.
        var invariantContrast = contrast.ToString(CultureInfo.InvariantCulture);
        var invariantMeanBrightness = meanBrightness.ToString(CultureInfo.InvariantCulture);
        var invariantStdDev = stdDev.ToString(CultureInfo.InvariantCulture);
        var grayscaleJson = grayscale ? "true" : "false";

        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {
              "image_base64": "{{TinyPngBase64}}",
              "content_type": "image/png",
              "width": {{width}},
              "height": {{height}},
              "original_width": {{originalWidth}},
              "original_height": {{originalHeight}},
              "effective_params": {
                "grayscale": {{grayscaleJson}},
                "contrast": {{invariantContrast}},
                "brightness": {{brightness}},
                "denoise": {{denoise}}
              },
              "metrics": {
                "mean_brightness": {{invariantMeanBrightness}},
                "std_dev": {{invariantStdDev}},
                "min_value": {{minValue}},
                "max_value": {{maxValue}}
              }
            }
            """);
    }

    public static string ErrorBody(string code, string message) =>
        $$"""{"code": "{{code}}", "message": "{{message}}"}""";
}
