using System.Text.Json;
using Vectify.Api.Contracts;

namespace Vectify.Api.Clients;

/// <summary>
/// Implementación de <see cref="IPythonVectorizationClient"/> sobre un HttpClient tipado
/// (registrado vía IHttpClientFactory). BaseAddress y timeout se configuran externamente
/// a partir de <see cref="Vectify.Api.Options.PythonEngineOptions"/>.
/// </summary>
public sealed class PythonVectorizationClient : IPythonVectorizationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonVectorizationClient> _logger;

    public PythonVectorizationClient(HttpClient httpClient, ILogger<PythonVectorizationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PythonHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync("/health", cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout expiró: se manifiesta como OperationCanceledException/TaskCanceledException
            // que NO fue pedida por el caller (el CancellationToken externo sigue sin cancelarse).
            _logger.LogWarning(
                "Timeout al consultar el motor Python en {BaseAddress}",
                _httpClient.BaseAddress);
            return new PythonHealthCheckResult(
                PythonHealthState.Timeout, null, null,
                "Tiempo de espera agotado al contactar al motor Python.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Motor Python no disponible en {BaseAddress}",
                _httpClient.BaseAddress);
            return new PythonHealthCheckResult(
                PythonHealthState.Unavailable, null, null,
                "No se pudo establecer conexión con el motor Python.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "El motor Python respondió con código {StatusCode}",
                    (int)response.StatusCode);
                return new PythonHealthCheckResult(
                    PythonHealthState.HttpError, null, null,
                    $"El motor Python respondió con código HTTP {(int)response.StatusCode}.");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Timeout al leer la respuesta del motor Python");
                return new PythonHealthCheckResult(
                    PythonHealthState.Timeout, null, null,
                    "Tiempo de espera agotado al leer la respuesta del motor Python.");
            }

            PythonHealthPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PythonHealthPayload>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Respuesta no-JSON del motor Python: {Body}", body);
                return new PythonHealthCheckResult(
                    PythonHealthState.InvalidResponse, null, null,
                    "La respuesta del motor Python no es un JSON válido.");
            }

            if (payload is null
                || string.IsNullOrWhiteSpace(payload.Status)
                || string.IsNullOrWhiteSpace(payload.Service)
                || string.IsNullOrWhiteSpace(payload.Version))
            {
                _logger.LogWarning("Respuesta incompleta del motor Python: {Body}", body);
                return new PythonHealthCheckResult(
                    PythonHealthState.InvalidResponse, payload?.Service, payload?.Version,
                    "La respuesta del motor Python no contiene los campos esperados (status, service, version).");
            }

            if (!string.Equals(payload.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new PythonHealthCheckResult(
                    PythonHealthState.HttpError, payload.Service, payload.Version,
                    $"El motor Python reportó status '{payload.Status}'.");
            }

            return new PythonHealthCheckResult(PythonHealthState.Online, payload.Service, payload.Version, null);
        }
    }
}
