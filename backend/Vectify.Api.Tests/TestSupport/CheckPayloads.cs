using System.Globalization;

namespace Vectify.Api.Tests.TestSupport;

/// <summary>Cuerpos JSON crudos (snake_case, como los devuelve Pydantic) para simular al motor Python en tests del Laser Checker de paths.</summary>
internal static class CheckPayloads
{
    public static string OpenPathIssueJson(
        string id = "open-0-0",
        string severity = "error",
        int pathIndex = 0,
        int subpathIndex = 0,
        double startX = 0,
        double startY = 0,
        double endX = 0.02,
        double endY = 0.01,
        double gapDistance = 0.022,
        double minX = 0,
        double minY = 0,
        double maxX = 10,
        double maxY = 10) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        {
          "type": "open_path",
          "id": "{{id}}",
          "severity": "{{severity}}",
          "path_index": {{pathIndex}},
          "subpath_index": {{subpathIndex}},
          "start_point": [{{startX}}, {{startY}}],
          "end_point": [{{endX}}, {{endY}}],
          "gap_distance": {{gapDistance}},
          "bounds": {"min_x": {{minX}}, "min_y": {{minY}}, "max_x": {{maxX}}, "max_y": {{maxY}}}
        }
        """);

    public static string DuplicatePathIssueJson(
        string id = "dup-1",
        string severity = "error",
        bool exact = true,
        double maxPointDistance = 0,
        int memberCount = 2) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        {
          "type": "duplicate_path",
          "id": "{{id}}",
          "severity": "{{severity}}",
          "exact": {{(exact ? "true" : "false")}},
          "max_point_distance": {{maxPointDistance}},
          "members": [{{string.Join(",", Enumerable.Range(0, memberCount).Select(MemberJson))}}]
        }
        """);

    private static string MemberJson(int pathIndex) => string.Create(
        CultureInfo.InvariantCulture,
        $"{{\"path_index\": {pathIndex}, \"subpath_index\": 0, \"bounds\": {{\"min_x\": 0, \"min_y\": 0, \"max_x\": 5, \"max_y\": 5}}}}");

    public static string SuccessBody(
        double closeGapRatio = 0.005,
        double duplicatePointRatio = 0.002,
        int openPathCount = 1,
        int duplicateGroupCount = 1,
        int skippedPathCount = 0,
        string? issuesJson = null)
    {
        var issues = issuesJson ?? $"[{OpenPathIssueJson()},{DuplicatePathIssueJson()}]";
        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {
              "effective_params": {"close_gap_ratio": {{closeGapRatio}}, "duplicate_point_ratio": {{duplicatePointRatio}}},
              "summary": {"open_path_count": {{openPathCount}}, "duplicate_group_count": {{duplicateGroupCount}}},
              "issues": {{issues}},
              "skipped_path_count": {{skippedPathCount}}
            }
            """);
    }

    public static string ErrorBody(string code, string message) =>
        $$"""{"code": "{{code}}", "message": "{{message}}"}""";
}
