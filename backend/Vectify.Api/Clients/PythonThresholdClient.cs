using System.Net.Http.Headers;
using System.Text.Json;
using Vectify.Api.Contracts;
using Vectify.Api.Threshold;

namespace Vectify.Api.Clients;

/// <summary>
/// Implementación de <see cref="IPythonThresholdClient"/> sobre un HttpClient
/// tipado propio (distinto del usado para preprocesamiento y para el chequeo
/// de salud): tiene su propio timeout configurable (Threshold:TimeoutSeconds).
/// </summary>
public sealed class PythonThresholdClient : IPythonThresholdClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonThresholdClient> _logger;

    public PythonThresholdClient(HttpClient httpClient, ILogger<PythonThresholdClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PythonThresholdResult> ThresholdAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        ThresholdParameters parameters,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(imageContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var paramsJson = JsonSerializer.Serialize(
            new
            {
                value = parameters.Value,
                invert = parameters.Invert,
            },
            JsonOptions);
        form.Add(new StringContent(paramsJson), "params");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("/api/v1/threshold", form, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timeout al generar la máscara en el motor Python en {BaseAddress}",
                _httpClient.BaseAddress);
            return Failure(PythonThresholdState.Timeout, "Tiempo de espera agotado al generar la máscara.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Motor Python no disponible en {BaseAddress}", _httpClient.BaseAddress);
            return Failure(PythonThresholdState.Unavailable, "No se pudo establecer conexión con el motor Python.");
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
                return Failure(PythonThresholdState.Timeout, "Tiempo de espera agotado al leer la respuesta del motor Python.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapErrorResponse(response.StatusCode, body);
            }

            PythonThresholdPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PythonThresholdPayload>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Respuesta no-JSON del motor Python al generar máscara: {Body}", body);
                return Failure(PythonThresholdState.InvalidResponse, "La respuesta del motor Python no es un JSON válido.");
            }

            if (payload?.ImageBase64 is null || payload.EffectiveParams is null || payload.Metrics is null)
            {
                _logger.LogWarning("Respuesta incompleta del motor Python al generar máscara: {Body}", body);
                return Failure(PythonThresholdState.InvalidResponse, "La respuesta del motor Python no contiene los campos esperados.");
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(payload.ImageBase64);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "image_base64 inválido en la respuesta del motor Python");
                return Failure(PythonThresholdState.InvalidResponse, "La máscara devuelta por el motor Python no es base64 válido.");
            }

            return new PythonThresholdResult(
                PythonThresholdState.Success,
                imageBytes,
                payload.ContentType ?? "image/png",
                payload.Width,
                payload.Height,
                new ThresholdParameters(payload.EffectiveParams.Value, payload.EffectiveParams.Invert),
                new ThresholdRawMetrics(payload.Metrics.ForegroundPercent, payload.Metrics.BackgroundPercent),
                Message: null);
        }
    }

    private PythonThresholdResult MapErrorResponse(System.Net.HttpStatusCode statusCode, string body)
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
            ("corrupt_image", _) => PythonThresholdState.CorruptImage,
            ("dimensions_exceeded", _) => PythonThresholdState.DimensionsExceeded,
            ("invalid_parameters", _) => PythonThresholdState.InvalidParameters,
            (_, 400) => PythonThresholdState.CorruptImage,
            (_, 413) => PythonThresholdState.DimensionsExceeded,
            (_, 422) => PythonThresholdState.InvalidParameters,
            _ => PythonThresholdState.HttpError,
        };

        var message = errorPayload?.Message ?? $"El motor Python respondió con código HTTP {(int)statusCode}.";

        if (state == PythonThresholdState.HttpError)
        {
            _logger.LogWarning("El motor Python respondió con código {StatusCode} al generar máscara", (int)statusCode);
        }

        return Failure(state, message);
    }

    private static PythonThresholdResult Failure(PythonThresholdState state, string message) =>
        new(state, null, null, null, null, null, null, message);
}
