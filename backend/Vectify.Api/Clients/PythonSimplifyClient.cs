using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Simplification;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Clients;

/// <summary>
/// Implementación de <see cref="IPythonSimplifyClient"/> sobre un HttpClient
/// tipado propio (distinto del usado para las demás etapas): tiene su propio
/// timeout configurable (Simplification:TimeoutSeconds). Igual que
/// PythonVectorizeClient, aplica defensa en profundidad adicional sobre la
/// respuesta de Python (Defecto 3 de la ronda de QA sobre M1-S05/M1-S06):
/// Vectify.Api nunca confía ciegamente en su caller, aunque Python ya
/// sanitice/valide del lado suyo.
/// </summary>
public sealed class PythonSimplifyClient : IPythonSimplifyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const double PercentToleranceUnits = 0.01;
    private const string ExpectedContentType = "image/svg+xml";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonSimplifyClient> _logger;
    private readonly int _maxSvgResponseBytes;

    public PythonSimplifyClient(HttpClient httpClient, IOptions<SimplificationOptions> options, ILogger<PythonSimplifyClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _maxSvgResponseBytes = options.Value.MaxSvgResponseBytes;
    }

    public async Task<PythonSimplifyResult> SimplifyAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        SimplificationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(svgContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var paramsJson = JsonSerializer.Serialize(new { epsilon_ratio = parameters.EpsilonRatio }, JsonOptions);
        form.Add(new StringContent(paramsJson), "params");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("/api/v1/simplify", form, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timeout al simplificar en el motor Python en {BaseAddress}",
                _httpClient.BaseAddress);
            return Failure(PythonSimplifyState.Timeout, "Tiempo de espera agotado al simplificar el SVG.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Motor Python no disponible en {BaseAddress}", _httpClient.BaseAddress);
            return Failure(PythonSimplifyState.Unavailable, "No se pudo establecer conexión con el motor Python.");
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
                return Failure(PythonSimplifyState.Timeout, "Tiempo de espera agotado al leer la respuesta del motor Python.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapErrorResponse(response.StatusCode, body);
            }

            PythonSimplifyPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PythonSimplifyPayload>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Respuesta no-JSON del motor Python al simplificar: {Body}", body);
                return Failure(PythonSimplifyState.InvalidResponse, "La respuesta del motor Python no es un JSON válido.");
            }

            if (string.IsNullOrEmpty(payload?.Svg)
                || payload.Metrics?.Before?.Bounds is null
                || payload.Metrics?.After?.Bounds is null)
            {
                _logger.LogWarning("Respuesta incompleta del motor Python al simplificar: {Body}", body);
                return Failure(PythonSimplifyState.InvalidResponse, "La respuesta del motor Python no contiene los campos esperados.");
            }

            if (TryGetValidationFailureReason(payload, out var validationError))
            {
                _logger.LogWarning(
                    "Respuesta de simplificación rechazada por validación defensiva adicional: {Reason}. Body: {Body}",
                    validationError,
                    body);
                return Failure(PythonSimplifyState.InvalidSvg, validationError!);
            }

            var metrics = new SimplificationMetrics(
                ToVectorMetrics(payload.Metrics!.Before!),
                ToVectorMetrics(payload.Metrics!.After!),
                payload.Metrics!.ReductionPercent);

            return new PythonSimplifyResult(
                PythonSimplifyState.Success,
                payload.Svg,
                payload.ContentType ?? "image/svg+xml",
                metrics,
                Message: null);
        }
    }

    private static VectorMetrics ToVectorMetrics(PythonVectorMetricsPayload payload)
    {
        var bounds = payload.Bounds!;
        return new VectorMetrics(
            payload.PathCount,
            payload.ApproxNodeCount,
            new VectorBounds(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, bounds.Width, bounds.Height));
    }

    /// <summary>
    /// Defensa en profundidad adicional sobre una respuesta que ya pasó el chequeo de
    /// "campos presentes": Python nunca debería producir nada de esto (ya sanitiza/valida
    /// del lado de app.core.simplification_pipeline/svg_processing), pero Vectify.Api no
    /// confía ciegamente en su caller -- mismo criterio que PythonVectorizeClient. Devuelve
    /// true (con el motivo en <paramref name="reason"/>) si la respuesta debe rechazarse.
    /// </summary>
    private bool TryGetValidationFailureReason(PythonSimplifyPayload payload, out string? reason)
    {
        var svg = payload.Svg!;
        var svgByteCount = Encoding.UTF8.GetByteCount(svg);

        if (svgByteCount > _maxSvgResponseBytes)
        {
            reason = $"El SVG devuelto por el motor Python ({svgByteCount} bytes) supera el límite permitido ({_maxSvgResponseBytes} bytes).";
            return true;
        }

        try
        {
            var parsed = XDocument.Parse(svg);
            if (parsed.Root is null || !string.Equals(parsed.Root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
            {
                reason = "El SVG devuelto por el motor Python no tiene un elemento <svg> como raíz.";
                return true;
            }
        }
        catch (System.Xml.XmlException ex)
        {
            reason = $"El SVG devuelto por el motor Python no es XML bien formado: {ex.Message}";
            return true;
        }

        var before = payload.Metrics!.Before!;
        var after = payload.Metrics!.After!;

        if (before.PathCount < 0 || before.ApproxNodeCount < 0 || after.PathCount < 0 || after.ApproxNodeCount < 0)
        {
            reason = "Las métricas devueltas por el motor Python son negativas.";
            return true;
        }

        if (after.ApproxNodeCount > before.ApproxNodeCount)
        {
            reason = $"El nodeCount después de simplificar ({after.ApproxNodeCount}) es mayor que antes ({before.ApproxNodeCount}); Douglas-Peucker nunca debería aumentar el conteo de nodos.";
            return true;
        }

        if (payload.Metrics!.ReductionPercent < 0 - PercentToleranceUnits || payload.Metrics!.ReductionPercent > 100 + PercentToleranceUnits)
        {
            reason = $"El % de reducción devuelto por el motor Python está fuera de rango (0-100): {payload.Metrics!.ReductionPercent}.";
            return true;
        }

        foreach (var bounds in new[] { before.Bounds!, after.Bounds! })
        {
            var boundsValues = new[] { bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, bounds.Width, bounds.Height };
            if (boundsValues.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            {
                reason = "Los bounds devueltos por el motor Python contienen valores no finitos (NaN/Infinity).";
                return true;
            }

            if (bounds.MaxX < bounds.MinX || bounds.MaxY < bounds.MinY)
            {
                reason = $"Los bounds devueltos por el motor Python son incoherentes (max < min): min=({bounds.MinX},{bounds.MinY}), max=({bounds.MaxX},{bounds.MaxY}).";
                return true;
            }
        }

        if (!string.Equals(payload.ContentType, ExpectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"El Content-Type devuelto por el motor Python no es '{ExpectedContentType}' (recibido: '{payload.ContentType ?? "(ausente)"}').";
            return true;
        }

        reason = null;
        return false;
    }

    private PythonSimplifyResult MapErrorResponse(System.Net.HttpStatusCode statusCode, string body)
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
            ("invalid_input_svg", _) => PythonSimplifyState.InvalidInputSvg,
            ("svg_input_too_large", _) => PythonSimplifyState.SvgTooLarge,
            ("svg_output_too_large", _) => PythonSimplifyState.SvgTooLarge,
            ("invalid_parameters", _) => PythonSimplifyState.InvalidParameters,
            ("simplification_timeout", _) => PythonSimplifyState.Timeout,
            ("invalid_svg", _) => PythonSimplifyState.EngineError,
            (_, 400) => PythonSimplifyState.InvalidInputSvg,
            (_, 413) => PythonSimplifyState.SvgTooLarge,
            (_, 422) => PythonSimplifyState.InvalidParameters,
            (_, 504) => PythonSimplifyState.Timeout,
            _ => PythonSimplifyState.HttpError,
        };

        var message = errorPayload?.Message ?? $"El motor Python respondió con código HTTP {(int)statusCode}.";

        if (state == PythonSimplifyState.HttpError)
        {
            _logger.LogWarning("El motor Python respondió con código {StatusCode} al simplificar", (int)statusCode);
        }

        return Failure(state, message);
    }

    private static PythonSimplifyResult Failure(PythonSimplifyState state, string message) =>
        new(state, null, null, null, message);
}
