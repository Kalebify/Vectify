using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>
/// Forma cruda de la respuesta de GET /health del motor Python/FastAPI.
/// Contrato inicial: { "status": "ok", "service": "...", "version": "..." }.
/// </summary>
public sealed class PythonHealthPayload
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("service")]
    public string? Service { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
