using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Clients;

/// <summary>
/// Implementación de <see cref="IPythonVectorizeClient"/> sobre un HttpClient
/// tipado propio (distinto del usado para health/preprocess/threshold): tiene
/// su propio timeout configurable (Vectorize:TimeoutSeconds).
/// </summary>
public sealed class PythonVectorizeClient : IPythonVectorizeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Tolerancia para comparar Width/Height contra MaxX-MinX/MaxY-MinY: ambos se calculan
    // por resta de los mismos bounds del lado de Python (services/python-engine/app/core/
    // svg_processing.py), así que en el caso normal coinciden exactamente -- esta tolerancia
    // solo absorbe errores de redondeo de punto flotante en la serialización JSON, no
    // discrepancias reales entre ambos valores.
    private const double BoundsToleranceUnits = 0.01;
    private const string ExpectedContentType = "image/svg+xml";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonVectorizeClient> _logger;
    private readonly int _maxSvgResponseBytes;

    public PythonVectorizeClient(HttpClient httpClient, IOptions<VectorizeOptions> options, ILogger<PythonVectorizeClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _maxSvgResponseBytes = options.Value.MaxSvgResponseBytes;
    }

    public async Task<PythonVectorizeResult> VectorizeAsync(
        Stream maskContent,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(maskContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("/api/v1/vectorize", form, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timeout al vectorizar en el motor Python en {BaseAddress}",
                _httpClient.BaseAddress);
            return Failure(PythonVectorizeState.Timeout, "Tiempo de espera agotado al vectorizar la máscara.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Motor Python no disponible en {BaseAddress}", _httpClient.BaseAddress);
            return Failure(PythonVectorizeState.Unavailable, "No se pudo establecer conexión con el motor Python.");
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
                return Failure(PythonVectorizeState.Timeout, "Tiempo de espera agotado al leer la respuesta del motor Python.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapErrorResponse(response.StatusCode, body);
            }

            PythonVectorizePayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PythonVectorizePayload>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Respuesta no-JSON del motor Python al vectorizar: {Body}", body);
                return Failure(PythonVectorizeState.InvalidResponse, "La respuesta del motor Python no es un JSON válido.");
            }

            if (string.IsNullOrEmpty(payload?.Svg) || payload.Metrics?.Bounds is null)
            {
                _logger.LogWarning("Respuesta incompleta del motor Python al vectorizar: {Body}", body);
                return Failure(PythonVectorizeState.InvalidResponse, "La respuesta del motor Python no contiene los campos esperados.");
            }

            if (TryGetValidationFailureReason(payload, out var validationError))
            {
                _logger.LogWarning(
                    "Respuesta de vectorización rechazada por validación defensiva adicional: {Reason}. Body: {Body}",
                    validationError,
                    body);
                return Failure(PythonVectorizeState.InvalidSvg, validationError!);
            }

            var bounds = payload.Metrics.Bounds;

            return new PythonVectorizeResult(
                PythonVectorizeState.Success,
                payload.Svg,
                payload.ContentType ?? "image/svg+xml",
                payload.Width,
                payload.Height,
                new VectorMetrics(
                    payload.Metrics.PathCount,
                    payload.Metrics.ApproxNodeCount,
                    new VectorBounds(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, bounds.Width, bounds.Height)),
                Message: null);
        }
    }

    /// <summary>
    /// Defensa en profundidad adicional sobre una respuesta que ya pasó el chequeo de
    /// "campos presentes": Python/VTracer nunca debería producir nada de esto (el motor
    /// ya sanitiza/valida del lado de app.core.svg_processing), pero Vectify.Api no confía
    /// ciegamente en su caller -- ver Defecto 3 de la ronda de QA sobre M1-S05/M1-S06.
    /// Devuelve true (con el motivo en <paramref name="reason"/>) si la respuesta debe
    /// rechazarse.
    /// </summary>
    private bool TryGetValidationFailureReason(PythonVectorizePayload payload, out string? reason)
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

        if (payload.Width <= 0 || payload.Height <= 0)
        {
            reason = $"Las dimensiones devueltas por el motor Python no son positivas (width={payload.Width}, height={payload.Height}).";
            return true;
        }

        var metrics = payload.Metrics!;
        if (metrics.PathCount < 0 || metrics.ApproxNodeCount < 0)
        {
            reason = $"Las métricas devueltas por el motor Python son negativas (path_count={metrics.PathCount}, approx_node_count={metrics.ApproxNodeCount}).";
            return true;
        }

        var bounds = metrics.Bounds!;
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

        if (Math.Abs((bounds.MaxX - bounds.MinX) - bounds.Width) > BoundsToleranceUnits
            || Math.Abs((bounds.MaxY - bounds.MinY) - bounds.Height) > BoundsToleranceUnits)
        {
            reason = $"Los bounds devueltos por el motor Python son incoherentes: width/height no coinciden con max-min (min=({bounds.MinX},{bounds.MinY}), max=({bounds.MaxX},{bounds.MaxY}), width={bounds.Width}, height={bounds.Height}).";
            return true;
        }

        if (!string.Equals(payload.ContentType, ExpectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"El Content-Type devuelto por el motor Python no es '{ExpectedContentType}' (recibido: '{payload.ContentType ?? "(ausente)"}').";
            return true;
        }

        reason = null;
        return false;
    }

    private PythonVectorizeResult MapErrorResponse(System.Net.HttpStatusCode statusCode, string body)
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
            ("corrupt_image", _) => PythonVectorizeState.CorruptImage,
            ("dimensions_exceeded", _) => PythonVectorizeState.DimensionsExceeded,
            ("svg_output_too_large", _) => PythonVectorizeState.DimensionsExceeded,
            ("empty_mask", _) => PythonVectorizeState.EmptyMask,
            ("invalid_parameters", _) => PythonVectorizeState.InvalidParameters,
            ("vectorization_timeout", _) => PythonVectorizeState.Timeout,
            ("vectorization_engine_error", _) => PythonVectorizeState.EngineError,
            ("invalid_svg", _) => PythonVectorizeState.EngineError,
            (_, 400) => PythonVectorizeState.CorruptImage,
            (_, 413) => PythonVectorizeState.DimensionsExceeded,
            (_, 422) => PythonVectorizeState.EmptyMask,
            (_, 504) => PythonVectorizeState.Timeout,
            _ => PythonVectorizeState.HttpError,
        };

        var message = errorPayload?.Message ?? $"El motor Python respondió con código HTTP {(int)statusCode}.";

        if (state == PythonVectorizeState.HttpError)
        {
            _logger.LogWarning("El motor Python respondió con código {StatusCode} al vectorizar", (int)statusCode);
        }

        return Failure(state, message);
    }

    private static PythonVectorizeResult Failure(PythonVectorizeState state, string message) =>
        new(state, null, null, null, null, null, message);
}
