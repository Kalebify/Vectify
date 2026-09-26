using System.Globalization;

namespace Vectify.Api.Tests.TestSupport;

/// <summary>Cuerpos JSON crudos (snake_case, como los devuelve Pydantic) para simular al motor Python en tests del análisis de componentes físicos (M2-S03).</summary>
internal static class ComponentPayloads
{
    public static string MemberJson(
        int pathIndex = 0, int subpathIndex = 0, string role = "solid", double area = 36,
        double minX = 2, double minY = 2, double maxX = 8, double maxY = 8) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        {
          "path_index": {{pathIndex}},
          "subpath_index": {{subpathIndex}},
          "role": "{{role}}",
          "bounds": {"min_x": {{minX}}, "min_y": {{minY}}, "max_x": {{maxX}}, "max_y": {{maxY}}},
          "area": {{area}}
        }
        """);

    public static string ComponentJson(
        string id = "component-1",
        string? membersJson = null,
        double area = 36,
        bool isTiny = false,
        double minX = 2,
        double minY = 2,
        double maxX = 8,
        double maxY = 8) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        {
          "id": "{{id}}",
          "members": [{{membersJson ?? MemberJson()}}],
          "bounds": {"min_x": {{minX}}, "min_y": {{minY}}, "max_x": {{maxX}}, "max_y": {{maxY}}},
          "area": {{area}},
          "is_tiny": {{(isTiny ? "true" : "false")}}
        }
        """);

    public static string SuccessBody(
        double touchRatio = 0.001,
        double tinyAreaRatio = 0.0005,
        int componentCount = 1,
        int tinyComponentCount = 0,
        int skippedPathCount = 0,
        string? componentsJson = null)
    {
        var components = componentsJson ?? $"[{ComponentJson()}]";
        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {
              "effective_params": {"touch_ratio": {{touchRatio}}, "tiny_area_ratio": {{tinyAreaRatio}}},
              "summary": {"component_count": {{componentCount}}, "tiny_component_count": {{tinyComponentCount}}},
              "components": {{components}},
              "skipped_path_count": {{skippedPathCount}}
            }
            """);
    }

    public static string ErrorBody(string code, string message) =>
        $$"""{"code": "{{code}}", "message": "{{message}}"}""";
}
