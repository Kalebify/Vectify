using System.Net.Http.Headers;
using System.Text.Json;
using Vectify.Api.Contracts;
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

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonVectorizeClient> _logger;

    public PythonVectorizeClient(HttpClient httpClient, ILogger<PythonVectorizeClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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
