namespace Vectify.Api.Clients;

/// <summary>
/// Cliente tipado hacia el endpoint de componentes físicos independientes
/// por capa (POST /api/v1/components) del motor Python/FastAPI. Cada etapa
/// del pipeline tiene su propio cliente con su propio timeout (mismo patrón
/// que IPythonCheckClient/IPythonVectorLayerClient).
/// </summary>
public interface IPythonComponentClient
{
    /// <summary>
    /// Envía el SVG de una capa YA generada (una VectorVersion existente).
    /// Las tolerancias de "tocarse"/"diminuto" NO son ajustables desde
    /// Vectify.Api en este sprint -- Python aplica sus propios defaults (ver
    /// app.core.config.Settings). Nunca lanza excepciones: cualquier falla
    /// (offline, timeout, SVG de entrada inválido/demasiado grande/con
    /// demasiados subpaths, respuesta inválida, error HTTP) se traduce a un
    /// <see cref="PythonComponentResult"/> con el estado correspondiente.
    /// </summary>
    Task<PythonComponentResult> AnalyzeAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default);
}
