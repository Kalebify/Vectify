using System.Net.Http.Headers;
using System.Text.Json;
using Vectify.Api.Components;
using Vectify.Api.Contracts;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Clients;

/// <summary>
/// Implementación de <see cref="IPythonPhysicalUnionClient"/> sobre un
/// HttpClient tipado propio: tiene su propio timeout configurable
/// (PhysicalUnion:TimeoutSeconds). Igual que PythonComponentClient/
/// PythonVectorizeClient, aplica defensa en profundidad adicional sobre la
/// respuesta de Python: Vectify.Api nunca confía ciegamente en su caller.
/// </summary>
public sealed class PythonPhysicalUnionClient : IPythonPhysicalUnionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> KnownStrategies = new(StringComparer.Ordinal) { "boolean_union", "bridge", "mixed" };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonPhysicalUnionClient> _logger;

    public PythonPhysicalUnionClient(HttpClient httpClient, ILogger<PythonPhysicalUnionClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PythonPhysicalUnionResult> UnionAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        IReadOnlyList<LayerComponent> selectedComponents,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(svgContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var requestPayload = new PythonPhysicalUnionRequestPayload
        {
            Selections = selectedComponents
                .Select(component => new PythonPhysicalUnionSelectionPayload
                {
                    ComponentId = component.Id,
                    Members = component.Members
                        .Select(member => new PythonPhysicalUnionMemberPayload
                        {
                            PathIndex = member.PathIndex,
                            SubpathIndex = member.SubpathIndex,
                            Role = member.Role,
                        })
                        .ToList(),
                })
                .ToList(),
        };
        form.Add(new StringContent(JsonSerializer.Serialize(requestPayload)), "params");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("/api/v1/components/union", form, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timeout al unir físicamente componentes en el motor Python en {BaseAddress}",
                _httpClient.BaseAddress);
            return Failure(PythonPhysicalUnionState.Timeout, "Tiempo de espera agotado al unir físicamente las piezas.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Motor Python no disponible en {BaseAddress}", _httpClient.BaseAddress);
            return Failure(PythonPhysicalUnionState.Unavailable, "No se pudo establecer conexión con el motor Python.");
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
                return Failure(PythonPhysicalUnionState.Timeout, "Tiempo de espera agotado al leer la respuesta del motor Python.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapErrorResponse(response.StatusCode, body);
            }

            PythonPhysicalUnionPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PythonPhysicalUnionPayload>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Respuesta no-JSON del motor Python al unir componentes: {Body}", body);
                return Failure(PythonPhysicalUnionState.InvalidResponse, "La respuesta del motor Python no es un JSON válido.");
            }

            if (string.IsNullOrEmpty(payload?.Svg) || payload.Metrics?.Bounds is null || payload.Strategy is null)
            {
                _logger.LogWarning("Respuesta incompleta del motor Python al unir componentes: {Body}", body);
                return Failure(PythonPhysicalUnionState.InvalidResponse, "La respuesta del motor Python no contiene los campos esperados.");
            }

            if (TryGetValidationFailureReason(payload, out var validationError))
            {
                _logger.LogWarning(
                    "Respuesta de unión física rechazada por validación defensiva adicional: {Reason}. Body: {Body}",
                    validationError,
                    body);
                return Failure(PythonPhysicalUnionState.InvalidResponse, validationError!);
            }

            var bounds = payload.Metrics.Bounds;

            return new PythonPhysicalUnionResult(
                PythonPhysicalUnionState.Success,
                payload.Svg,
                payload.ContentType ?? "image/svg+xml",
                payload.Width,
                payload.Height,
                new VectorMetrics(
                    payload.Metrics.PathCount,
                    payload.Metrics.ApproxNodeCount,
                    new VectorBounds(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, bounds.Width, bounds.Height)),
                payload.ComponentCountBefore,
                payload.ComponentCountAfter,
                payload.Strategy,
                payload.BridgeCount,
                Message: null);
        }
    }

    /// <summary>
    /// Defensa en profundidad adicional: Python nunca debería producir nada
    /// de esto (ya valida del lado de app.core.physical_union/
    /// app.models.schemas -- en particular, `component_count_after` YA
    /// coincide con `expected_component_count_after` porque si no
    /// coincidiera Python habría respondido 422 en vez de 200, ver
    /// PhysicalUnionImpossibleError), pero Vectify.Api no confía ciegamente
    /// en su caller.
    /// </summary>
    private bool TryGetValidationFailureReason(PythonPhysicalUnionPayload payload, out string? reason)
    {
        if (payload.Width <= 0 || payload.Height <= 0)
        {
            reason = $"Las dimensiones devueltas por el motor Python no son positivas (width={payload.Width}, height={payload.Height}).";
            return true;
        }

        if (!KnownStrategies.Contains(payload.Strategy!))
        {
            reason = $"La estrategia devuelta por el motor Python no es reconocida: '{payload.Strategy}'.";
            return true;
        }

        if (payload.ComponentCountBefore < 0 || payload.ComponentCountAfter < 0 || payload.BridgeCount < 0)
        {
            reason = "Los conteos devueltos por el motor Python son negativos.";
            return true;
        }

        if (payload.ComponentCountAfter != payload.ExpectedComponentCountAfter)
        {
            reason =
                $"El motor Python respondió éxito (200) pero component_count_after ({payload.ComponentCountAfter}) " +
                $"no coincide con expected_component_count_after ({payload.ExpectedComponentCountAfter}) -- " +
                "nunca se debe confiar en una unión que 'parece' exitosa pero no cumple su propia validación.";
            return true;
        }

        var bounds = payload.Metrics!.Bounds!;
        var boundsValues = new[] { bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, bounds.Width, bounds.Height };
        if (boundsValues.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
        {
            reason = "Los bounds devueltos por el motor Python contienen valores no finitos (NaN/Infinity).";
            return true;
        }

        if (bounds.MaxX < bounds.MinX || bounds.MaxY < bounds.MinY)
        {
            reason = "Los bounds devueltos por el motor Python son incoherentes (max < min).";
            return true;
        }

        reason = null;
        return false;
    }

    private PythonPhysicalUnionResult MapErrorResponse(System.Net.HttpStatusCode statusCode, string body)
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
            ("invalid_input_svg", _) => PythonPhysicalUnionState.InvalidInputSvg,
            ("svg_input_too_large", _) => PythonPhysicalUnionState.SvgTooLarge,
            ("too_many_component_subpaths", _) => PythonPhysicalUnionState.SvgTooLarge,
            ("invalid_parameters", _) => PythonPhysicalUnionState.InvalidParameters,
            ("physical_union_invalid_geometry", _) => PythonPhysicalUnionState.InvalidGeometry,
            ("physical_union_impossible", _) => PythonPhysicalUnionState.Impossible,
            ("physical_union_timeout", _) => PythonPhysicalUnionState.Timeout,
            (_, 400) => PythonPhysicalUnionState.InvalidInputSvg,
            (_, 413) => PythonPhysicalUnionState.SvgTooLarge,
            (_, 422) => PythonPhysicalUnionState.InvalidParameters,
            (_, 504) => PythonPhysicalUnionState.Timeout,
            _ => PythonPhysicalUnionState.HttpError,
        };

        var message = errorPayload?.Message ?? $"El motor Python respondió con código HTTP {(int)statusCode}.";

        if (state == PythonPhysicalUnionState.HttpError)
        {
            _logger.LogWarning("El motor Python respondió con código {StatusCode} al unir componentes", (int)statusCode);
        }

        return Failure(state, message);
    }

    private static PythonPhysicalUnionResult Failure(PythonPhysicalUnionState state, string message) =>
        new(state, null, null, null, null, null, null, null, null, null, message);
}
