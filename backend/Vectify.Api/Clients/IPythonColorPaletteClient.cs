using Vectify.Api.ColorPalette;

namespace Vectify.Api.Clients;

/// <summary>
/// Cliente tipado hacia el endpoint de detección/reducción de paleta de
/// colores (POST /api/v1/color-palette) del motor Python/FastAPI. Mismo
/// patrón que IPythonSimplifyClient/IPythonThresholdClient: cada etapa tiene
/// su propio cliente con su propio timeout.
/// </summary>
public interface IPythonColorPaletteClient
{
    /// <summary>
    /// Envía la imagen original (RGBA soportado) y los parámetros de
    /// detección. Nunca lanza excepciones: cualquier falla (offline, timeout,
    /// imagen inválida/demasiado grande, respuesta inválida, error HTTP) se
    /// traduce a un <see cref="PythonColorPaletteResult"/> con el estado
    /// correspondiente.
    /// </summary>
    Task<PythonColorPaletteResult> DetectAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        ColorPaletteParameters parameters,
        CancellationToken cancellationToken = default);
}
