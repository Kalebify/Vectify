using Vectorify.Api.Clients;
using Vectorify.Api.ColorPalette;
using Vectorify.Api.Tests.TestSupport;

namespace Vectorify.Api.Tests.ColorPalette;

/// <summary>IPythonColorPaletteClient en memoria para tests unitarios de ColorPaletteService.</summary>
internal sealed class FakePythonColorPaletteClient : IPythonColorPaletteClient
{
    private int _callCount;

    public int CallCount => _callCount;
    public Func<PythonColorPaletteResult>? Respond { get; set; }

    /// <summary>Retraso artificial antes de responder, para forzar solapamiento en tests de concurrencia.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<PythonColorPaletteResult> DetectAsync(
        Stream imageContent,
        string contentType,
        string fileName,
        ColorPaletteParameters parameters,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return Respond?.Invoke() ?? DefaultSuccess();
    }

    // 4x4: mitad izquierda pertenece al grupo 0 (rojo), mitad derecha al
    // grupo 1 (verde) -- máscaras reales y válidas, coincidentes con
    // Width/Height, para poder ejercitar MaskCompositor (merge/unmerge) de
    // punta a punta en ColorPaletteServiceTests.
    public static PythonColorPaletteResult DefaultSuccess() => new(
        PythonColorPaletteState.Success,
        Width: 4,
        Height: 4,
        ContentType: "image/png",
        Groups: new[]
        {
            // TouchesBorder=true para el grupo 0 (área más grande, empatada con el grupo 1 en este
            // fixture) -- ejercita el caso más común de la heurística de fondo dominante
            // (ColorPaletteService.DetectAsync: IsExcluded solo se pre-marca en groups[0]).
            new PythonColorGroupResult(0, "#ff0000", 8, 50.0, false, TouchesBorder: true, MaskBytes: ColorPalettePngs.LeftHalfMask(4, 4)),
            new PythonColorGroupResult(1, "#00ff00", 8, 50.0, false, TouchesBorder: false, MaskBytes: ColorPalettePngs.RightHalfMask(4, 4)),
        },
        TransparentPercent: 0.0,
        QuantizedPreviewBytes: ColorPalettePngs.TransparentPreview(4, 4),
        Message: null);
}
