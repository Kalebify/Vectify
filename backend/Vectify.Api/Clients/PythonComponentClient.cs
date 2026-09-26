using System.Net.Http.Headers;
using System.Text.Json;
using Vectify.Api.Components;
using Vectify.Api.Contracts;

namespace Vectify.Api.Clients;

/// <summary>
/// Implementación de <see cref="IPythonComponentClient"/> sobre un
/// HttpClient tipado propio: tiene su propio timeout configurable
/// (Component:TimeoutSeconds). Igual que PythonCheckClient, aplica defensa
/// en profundidad adicional sobre la respuesta de Python: Vectify.Api nunca
/// confía ciegamente en su caller, aunque Python ya valide del lado suyo.
/// </summary>
public sealed class PythonComponentClient : IPythonComponentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> KnownRoles = new(StringComparer.Ordinal) { "solid", "hole" };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonComponentClient> _logger;

    public PythonComponentClient(HttpClient httpClient, ILogger<PythonComponentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PythonComponentResult> AnalyzeAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(svgContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        // Sin parámetros ajustables desde Vectify.Api en este sprint: se
        // envía un objeto vacío para que Python aplique sus propios
        // defaults (touch_ratio/tiny_area_ratio, ver ComponentAnalysisParams
        // del lado Python) -- única fuente de verdad de esos valores.
        form.Add(new StringContent("{}"), "params");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("/api/v1/components", form, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timeout al analizar componentes en el motor Python en {BaseAddress}",
                _httpClient.BaseAddress);
            return Failure(PythonComponentState.Timeout, "Tiempo de espera agotado al analizar los componentes de la capa.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Motor Python no disponible en {BaseAddress}", _httpClient.BaseAddress);
            return Failure(PythonComponentState.Unavailable, "No se pudo establecer conexión con el motor Python.");
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
                return Failure(PythonComponentState.Timeout, "Tiempo de espera agotado al leer la respuesta del motor Python.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapErrorResponse(response.StatusCode, body);
            }

            PythonComponentPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PythonComponentPayload>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Respuesta no-JSON del motor Python al analizar componentes: {Body}", body);
                return Failure(PythonComponentState.InvalidResponse, "La respuesta del motor Python no es un JSON válido.");
            }

            if (payload?.Summary is null || payload.Components is null || payload.EffectiveParams is null)
            {
                _logger.LogWarning("Respuesta incompleta del motor Python al analizar componentes: {Body}", body);
                return Failure(PythonComponentState.InvalidResponse, "La respuesta del motor Python no contiene los campos esperados.");
            }

            if (TryConvertComponents(payload, out var components, out var validationError))
            {
                return new PythonComponentResult(PythonComponentState.Success, components, payload.SkippedPathCount, Message: null);
            }

            _logger.LogWarning(
                "Respuesta del análisis de componentes rechazada por validación defensiva adicional: {Reason}. Body: {Body}",
                validationError,
                body);
            return Failure(PythonComponentState.InvalidSvg, validationError!);
        }
    }

    /// <summary>
    /// Defensa en profundidad adicional sobre una respuesta que ya pasó el chequeo de "campos
    /// presentes": Python nunca debería producir nada de esto (ya valida del lado de
    /// app.core.component_analysis/app.models.schemas), pero Vectify.Api no confía ciegamente en
    /// su caller. Devuelve true (con <paramref name="components"/> poblado) si la respuesta es
    /// coherente; false (con el motivo en <paramref name="reason"/>) si debe rechazarse.
    /// </summary>
    private bool TryConvertComponents(PythonComponentPayload payload, out List<LayerComponent> components, out string? reason)
    {
        components = new List<LayerComponent>();

        if (payload.SkippedPathCount < 0)
        {
            reason = "skipped_path_count devuelto por el motor Python es negativo.";
            return false;
        }

        var tinyCount = 0;

        foreach (var raw in payload.Components!)
        {
            if (string.IsNullOrEmpty(raw.Id) || raw.Bounds is null || raw.Area is not >= 0 || raw.IsTiny is null
                || raw.Members is not { Count: >= 1 })
            {
                reason = $"Componente con id/bounds/area/is_tiny/members ausente o incoherente: '{raw.Id}'.";
                return false;
            }

            if (!AreFinite(raw.Bounds) || raw.Bounds.MaxX < raw.Bounds.MinX || raw.Bounds.MaxY < raw.Bounds.MinY)
            {
                reason = $"Componente '{raw.Id}' con bounds incoherentes.";
                return false;
            }

            var members = new List<ComponentMember>(raw.Members.Count);
            foreach (var member in raw.Members)
            {
                if (member.PathIndex < 0 || member.SubpathIndex < 0 || member.Bounds is null
                    || member.Area is not >= 0 || !KnownRoles.Contains(member.Role ?? string.Empty)
                    || !AreFinite(member.Bounds) || member.Bounds.MaxX < member.Bounds.MinX || member.Bounds.MaxY < member.Bounds.MinY)
                {
                    reason = $"Miembro del componente '{raw.Id}' con forma incoherente.";
                    return false;
                }

                members.Add(new ComponentMember(
                    member.PathIndex,
                    member.SubpathIndex,
                    member.Role!,
                    new ComponentBounds(member.Bounds.MinX, member.Bounds.MinY, member.Bounds.MaxX, member.Bounds.MaxY),
                    member.Area.Value));
            }

            if (raw.IsTiny.Value)
            {
                tinyCount++;
            }

            components.Add(new LayerComponent(
                raw.Id!,
                members,
                new ComponentBounds(raw.Bounds.MinX, raw.Bounds.MinY, raw.Bounds.MaxX, raw.Bounds.MaxY),
                raw.Area.Value,
                raw.IsTiny.Value));
        }

        if (components.Count != payload.Summary!.ComponentCount || tinyCount != payload.Summary.TinyComponentCount)
        {
            reason = $"El resumen (count={payload.Summary.ComponentCount}, tiny={payload.Summary.TinyComponentCount}) " +
                     $"no coincide con la cantidad real de componentes devueltos (count={components.Count}, tiny={tinyCount}).";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool AreFinite(PythonComponentBoundsPayload bounds) =>
        !new[] { bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY }.Any(v => double.IsNaN(v) || double.IsInfinity(v));

    private PythonComponentResult MapErrorResponse(System.Net.HttpStatusCode statusCode, string body)
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
            ("invalid_input_svg", _) => PythonComponentState.InvalidInputSvg,
            ("svg_input_too_large", _) => PythonComponentState.SvgTooLarge,
            ("too_many_component_subpaths", _) => PythonComponentState.TooManySubpaths,
            ("invalid_parameters", _) => PythonComponentState.InvalidParameters,
            ("component_analysis_timeout", _) => PythonComponentState.Timeout,
            (_, 400) => PythonComponentState.InvalidInputSvg,
            (_, 413) => PythonComponentState.SvgTooLarge,
            (_, 422) => PythonComponentState.InvalidParameters,
            (_, 504) => PythonComponentState.Timeout,
            _ => PythonComponentState.HttpError,
        };

        var message = errorPayload?.Message ?? $"El motor Python respondió con código HTTP {(int)statusCode}.";

        if (state == PythonComponentState.HttpError)
        {
            _logger.LogWarning("El motor Python respondió con código {StatusCode} al analizar componentes", (int)statusCode);
        }

        return Failure(state, message);
    }

    private static PythonComponentResult Failure(PythonComponentState state, string message) =>
        new(state, null, null, message);
}
