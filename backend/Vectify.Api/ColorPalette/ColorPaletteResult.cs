namespace Vectify.Api.ColorPalette;

/// <summary>
/// Resultado compartido por las cinco operaciones de
/// <see cref="IColorPaletteService"/> (detectar, fusionar, deshacer fusión,
/// renombrar, confirmar): todas producen -- o fallan en producir -- una
/// nueva <see cref="ColorPaletteVersion"/>, nunca mutan una existente.
/// </summary>
public abstract record ColorPaletteResult
{
    private ColorPaletteResult()
    {
    }

    /// <summary>Nueva versión lista: recién generada, o reutilizada desde caché (solo posible en DetectAsync).</summary>
    public sealed record Ready(ColorPaletteVersion Record, bool FromCache) : ColorPaletteResult;

    /// <summary>No existe el proyecto/imagen, la sesión (PaletteId) o el grupo (GroupId) referenciado (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : ColorPaletteResult;

    /// <summary>Los parámetros están fuera de rango, faltan, o son incoherentes (el endpoint responde 400/422).</summary>
    public sealed record ValidationFailed(string Code, string Message) : ColorPaletteResult;

    /// <summary>
    /// La operación es incompatible con el estado actual de la sesión: la paleta ya está
    /// confirmada (no se puede seguir editando) o el grupo indicado no proviene de un merge
    /// (nada que deshacer) -- el endpoint responde 409.
    /// </summary>
    public sealed record Conflict(string Code, string Message) : ColorPaletteResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : ColorPaletteResult;
}
