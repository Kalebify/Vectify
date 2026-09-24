namespace Vectify.Api.Tests.TestSupport;

/// <summary>Cuerpos JSON crudos (snake_case, como los devuelve Pydantic) para simular al motor Python en tests de threshold.</summary>
internal static class ThresholdPayloads
{
    /// <summary>Base64 de un PNG 1x1 válido (mismos bytes que SampleImages.ValidPng1x1).</summary>
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    public static string SuccessBody(
        int value = 128,
        bool invert = false,
        int width = 1,
        int height = 1,
        double foregroundPercent = 40.0,
        double backgroundPercent = 60.0)
    {
        var invertJson = invert ? "true" : "false";

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $$"""
            {
              "image_base64": "{{TinyPngBase64}}",
              "content_type": "image/png",
              "width": {{width}},
              "height": {{height}},
              "effective_params": {
                "value": {{value}},
                "invert": {{invertJson}}
              },
              "metrics": {
                "foreground_percent": {{foregroundPercent}},
                "background_percent": {{backgroundPercent}}
              }
            }
            """);
    }

    public static string ErrorBody(string code, string message) =>
        $$"""{"code": "{{code}}", "message": "{{message}}"}""";
}
