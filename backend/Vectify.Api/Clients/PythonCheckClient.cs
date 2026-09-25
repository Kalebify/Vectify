using System.Net.Http.Headers;
using System.Text.Json;
using Vectify.Api.Checking;
using Vectify.Api.Contracts;

namespace Vectify.Api.Clients;

/// <summary>
/// Implementación de <see cref="IPythonCheckClient"/> sobre un HttpClient
/// tipado propio (distinto del usado para las demás etapas): tiene su propio
/// timeout configurable (Check:TimeoutSeconds). Igual que
/// PythonSimplifyClient/PythonVectorizeClient, aplica defensa en profundidad
/// adicional sobre la respuesta de Python: Vectify.Api nunca confía
/// ciegamente en su caller, aunque Python ya valide del lado suyo.
/// </summary>
public sealed class PythonCheckClient : IPythonCheckClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> KnownSeverities = new(StringComparer.Ordinal) { "warning", "error" };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonCheckClient> _logger;

    public PythonCheckClient(HttpClient httpClient, ILogger<PythonCheckClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PythonCheckResult> CheckAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        CheckParameters parameters,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(svgContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var paramsJson = JsonSerializer.Serialize(
            new { close_gap_ratio = parameters.CloseGapRatio, duplicate_point_ratio = parameters.DuplicatePointRatio },
            JsonOptions);
        form.Add(new StringContent(paramsJson), "params");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("/api/v1/check", form, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timeout al analizar paths en el motor Python en {BaseAddress}",
                _httpClient.BaseAddress);
            return Failure(PythonCheckState.Timeout, "Tiempo de espera agotado al analizar el SVG.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Motor Python no disponible en {BaseAddress}", _httpClient.BaseAddress);
            return Failure(PythonCheckState.Unavailable, "No se pudo establecer conexión con el motor Python.");
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Failure(PythonCheckState.Timeout, "Tiempo de espera agotado al leer la respuesta del motor Python.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapErrorResponse(response.StatusCode, body);
            }

            PythonCheckPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PythonCheckPayload>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Respuesta no-JSON del motor Python al analizar paths: {Body}", body);
                return Failure(PythonCheckState.InvalidResponse, "La respuesta del motor Python no es un JSON válido.");
            }

            if (payload?.Summary is null || payload.Issues is null || payload.EffectiveParams is null)
            {
                _logger.LogWarning("Respuesta incompleta del motor Python al analizar paths: {Body}", body);
                return Failure(PythonCheckState.InvalidResponse, "La respuesta del motor Python no contiene los campos esperados.");
            }

            if (TryConvertIssues(payload, out var issues, out var validationError))
            {
                return new PythonCheckResult(PythonCheckState.Success, issues, payload.SkippedPathCount, Message: null);
            }

            _logger.LogWarning(
                "Respuesta del análisis de paths rechazada por validación defensiva adicional: {Reason}. Body: {Body}",
                validationError,
                body);
            return Failure(PythonCheckState.InvalidSvg, validationError!);
        }
    }

    /// <summary>
    /// Defensa en profundidad adicional sobre una respuesta que ya pasó el chequeo de "campos
    /// presentes": Python nunca debería producir nada de esto (ya valida del lado de
    /// app.core.path_checker/app.models.schemas), pero Vectify.Api no confía ciegamente en su
    /// caller. Devuelve true (con <paramref name="issues"/> poblado) si la respuesta es
    /// coherente; false (con el motivo en <paramref name="reason"/>) si debe rechazarse.
    /// </summary>
    private bool TryConvertIssues(PythonCheckPayload payload, out List<CheckIssue> issues, out string? reason)
    {
        issues = new List<CheckIssue>();

        var effectiveParams = payload.EffectiveParams!;
        if (double.IsNaN(effectiveParams.CloseGapRatio) || double.IsInfinity(effectiveParams.CloseGapRatio)
            || effectiveParams.CloseGapRatio <= 0 || effectiveParams.CloseGapRatio > 0.5
            || double.IsNaN(effectiveParams.DuplicatePointRatio) || double.IsInfinity(effectiveParams.DuplicatePointRatio)
            || effectiveParams.DuplicatePointRatio <= 0 || effectiveParams.DuplicatePointRatio > 0.5)
        {
            reason = "Las tolerancias efectivas devueltas por el motor Python están fuera de rango (0, 0.5].";
            return false;
        }

        if (payload.SkippedPathCount < 0)
        {
            reason = "skipped_path_count devuelto por el motor Python es negativo.";
            return false;
        }

        var openPathCount = 0;
        var duplicateGroupCount = 0;

        foreach (var raw in payload.Issues!)
        {
            if (string.IsNullOrEmpty(raw.Id) || !KnownSeverities.Contains(raw.Severity ?? string.Empty))
            {
                reason = $"Issue con id/severity ausente o desconocido: '{raw.Id}' / '{raw.Severity}'.";
                return false;
            }

            switch (raw.Type)
            {
                case "open_path":
                    if (!TryConvertOpenPath(raw, out var openPath, out reason))
                    {
                        return false;
                    }
                    issues.Add(openPath!);
                    openPathCount++;
                    break;

                case "duplicate_path":
                    if (!TryConvertDuplicatePath(raw, out var duplicatePath, out reason))
                    {
                        return false;
                    }
                    issues.Add(duplicatePath!);
                    duplicateGroupCount++;
                    break;

                default:
                    reason = $"Tipo de issue desconocido: '{raw.Type}'.";
                    return false;
            }
        }

        if (openPathCount != payload.Summary!.OpenPathCount || duplicateGroupCount != payload.Summary.DuplicateGroupCount)
        {
            reason = $"El resumen (open={payload.Summary.OpenPathCount}, dup={payload.Summary.DuplicateGroupCount}) " +
                     $"no coincide con la cantidad real de issues devueltos (open={openPathCount}, dup={duplicateGroupCount}).";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool TryConvertOpenPath(PythonCheckIssuePayload raw, out CheckIssue.OpenPath? issue, out string? reason)
    {
        issue = null;

        if (raw.PathIndex is not >= 0 || raw.SubpathIndex is not >= 0
            || raw.StartPoint is not { Count: 2 } || raw.EndPoint is not { Count: 2 }
            || raw.GapDistance is not >= 0 || raw.Bounds is null
            || !AreFinite(raw.Bounds) || raw.Bounds.MaxX < raw.Bounds.MinX || raw.Bounds.MaxY < raw.Bounds.MinY)
        {
            reason = $"Issue open_path '{raw.Id}' con forma incoherente.";
            return false;
        }

        issue = new CheckIssue.OpenPath(
            raw.Id!,
            raw.Severity!,
            raw.PathIndex.Value,
            raw.SubpathIndex.Value,
            (raw.StartPoint[0], raw.StartPoint[1]),
            (raw.EndPoint[0], raw.EndPoint[1]),
            raw.GapDistance.Value,
            new CheckBounds(raw.Bounds.MinX, raw.Bounds.MinY, raw.Bounds.MaxX, raw.Bounds.MaxY));
        reason = null;
        return true;
    }

    private static bool TryConvertDuplicatePath(PythonCheckIssuePayload raw, out CheckIssue.DuplicatePath? issue, out string? reason)
    {
        issue = null;

        if (raw.Exact is null || raw.MaxPointDistance is not >= 0 || raw.Members is not { Count: >= 2 })
        {
            reason = $"Issue duplicate_path '{raw.Id}' con forma incoherente.";
            return false;
        }

        var members = new List<CheckDuplicateMember>(raw.Members.Count);
        foreach (var member in raw.Members)
        {
            if (member.PathIndex < 0 || member.SubpathIndex < 0 || member.Bounds is null
                || !AreFinite(member.Bounds) || member.Bounds.MaxX < member.Bounds.MinX || member.Bounds.MaxY < member.Bounds.MinY)
            {
                reason = $"Miembro de duplicate_path '{raw.Id}' con forma incoherente.";
                return false;
            }

            members.Add(new CheckDuplicateMember(
                member.PathIndex, member.SubpathIndex,
                new CheckBounds(member.Bounds.MinX, member.Bounds.MinY, member.Bounds.MaxX, member.Bounds.MaxY)));
        }

        issue = new CheckIssue.DuplicatePath(raw.Id!, raw.Severity!, raw.Exact.Value, raw.MaxPointDistance.Value, members);
        reason = null;
        return true;
    }

    private static bool AreFinite(PythonCheckBoundsPayload bounds) =>
        !new[] { bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY }.Any(v => double.IsNaN(v) || double.IsInfinity(v));

    private PythonCheckResult MapErrorResponse(System.Net.HttpStatusCode statusCode, string body)
    {
        PythonErrorPayload? errorPayload;
        try
        {
            errorPayload = JsonSerializer.Deserialize<PythonErrorPayload>(body, JsonOptions);
        }
        catch (JsonException)
        {
            errorPayload = null;
        }

        var state = (errorPayload?.Code, (int)statusCode) switch
        {
            ("invalid_input_svg", _) => PythonCheckState.InvalidInputSvg,
            ("svg_input_too_large", _) => PythonCheckState.SvgTooLarge,
            ("too_many_subpaths", _) => PythonCheckState.TooManySubpaths,
            ("invalid_parameters", _) => PythonCheckState.InvalidParameters,
            ("check_timeout", _) => PythonCheckState.Timeout,
            (_, 400) => PythonCheckState.InvalidInputSvg,
            (_, 413) => PythonCheckState.SvgTooLarge,
            (_, 422) => PythonCheckState.InvalidParameters,
            (_, 504) => PythonCheckState.Timeout,
            _ => PythonCheckState.HttpError,
        };

        var message = errorPayload?.Message ?? $"El motor Python respondió con código HTTP {(int)statusCode}.";

        if (state == PythonCheckState.HttpError)
        {
            _logger.LogWarning("El motor Python respondió con código {StatusCode} al analizar paths", (int)statusCode);
        }

        return Failure(state, message);
    }

    private static PythonCheckResult Failure(PythonCheckState state, string message) =>
        new(state, null, null, message);
}
