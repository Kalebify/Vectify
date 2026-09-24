using System.Net.Http.Headers;
using System.Text.Json;
using Vectify.Api.Contracts;
using Vectify.Api.Preprocessing;

namespace Vectify.Api.Clients;

/// <summary>
/// Implementación de <see cref="IPythonPreprocessClient"/> sobre un HttpClient
/// tipado propio (distinto del usado para el chequeo de salud): el
/// preprocesamiento con OpenCV puede tardar más que un GET /health liviano, así
/// que tiene su propio timeout configurable (Preprocess:TimeoutSeconds) en vez
/// de compartir PythonEngine:TimeoutSeconds.
/// </summary>
public sealed class PythonPreprocessClient : IPythonPreprocessClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonPreprocessClient> _logger;

    public PythonPreprocessClient(HttpClient httpClient, ILogger<PythonPreprocessClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PythonPreprocessResult> PreprocessAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        PreprocessParameters parameters,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(imageContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var paramsJson = JsonSerializer.Serialize(
            new
            {
                grayscale = parameters.Grayscale,
                contrast = parameters.Contrast,
                brightness = parameters.Brightness,
                denoise = parameters.Denoise,
            },
            JsonOptions);
        form.Add(new StringContent(paramsJson), "params");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("/api/v1/preprocess", form, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timeout al generar el preview en el motor Python en {BaseAddress}",
                _httpClient.BaseAddress);
            return Failure(PythonPreprocessState.Timeout, "Tiempo de espera agotado al generar el preview.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Motor Python no disponible en {BaseAddress}", _httpClient.BaseAddress);
            return Failure(PythonPreprocessState.Unavailable, "No se pudo establecer conexión con el motor Python.");
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
                return Failure(PythonPreprocessState.Timeout, "Tiempo de espera agotado al leer la respuesta del motor Python.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapErrorResponse(response.StatusCode, body);
            }

            PythonPreprocessPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PythonPreprocessPayload>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Respuesta no-JSON del motor Python al generar preview: {Body}", body);
                return Failure(PythonPreprocessState.InvalidResponse, "La respuesta del motor Python no es un JSON válido.");
            }

            if (payload?.ImageBase64 is null || payload.EffectiveParams is null || payload.Metrics is null)
            {
                _logger.LogWarning("Respuesta incompleta del motor Python al generar preview: {Body}", body);
                return Failure(PythonPreprocessState.InvalidResponse, "La respuesta del motor Python no contiene los campos esperados.");
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(payload.ImageBase64);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "image_base64 inválido en la respuesta del motor Python");
                return Failure(PythonPreprocessState.InvalidResponse, "El preview devuelto por el motor Python no es base64 válido.");
            }

            return new PythonPreprocessResult(
                PythonPreprocessState.Success,
                imageBytes,
                payload.ContentType ?? "image/png",
                payload.Width,
                payload.Height,
                payload.OriginalWidth,
                payload.OriginalHeight,
                new PreprocessParameters(
                    payload.EffectiveParams.Grayscale,
                    payload.EffectiveParams.Contrast,
                    payload.EffectiveParams.Brightness,
                    payload.EffectiveParams.Denoise),
                new PreprocessMetrics(
                    payload.Metrics.MeanBrightness,
                    payload.Metrics.StdDev,
                    payload.Metrics.MinValue,
                    payload.Metrics.MaxValue),
                Message: null);
        }
    }

    private PythonPreprocessResult MapErrorResponse(System.Net.HttpStatusCode statusCode, string body)
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
            ("corrupt_image", _) => PythonPreprocessState.CorruptImage,
            ("dimensions_exceeded", _) => PythonPreprocessState.DimensionsExceeded,
            ("invalid_parameters", _) => PythonPreprocessState.InvalidParameters,
            (_, 400) => PythonPreprocessState.CorruptImage,
            (_, 413) => PythonPreprocessState.DimensionsExceeded,
            (_, 422) => PythonPreprocessState.InvalidParameters,
            _ => PythonPreprocessState.HttpError,
        };

        var message = errorPayload?.Message ?? $"El motor Python respondió con código HTTP {(int)statusCode}.";

        if (state == PythonPreprocessState.HttpError)
        {
            _logger.LogWarning("El motor Python respondió con código {StatusCode} al generar preview", (int)statusCode);
        }

        return Failure(state, message);
    }

    private static PythonPreprocessResult Failure(PythonPreprocessState state, string message) =>
        new(state, null, null, null, null, null, null, null, null, message);
}
