namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones de
/// paleta de colores (<see cref="Vectify.Api.ColorPalette.PersistentColorPaletteVersionRegistry"/>).
/// Se enlaza desde la sección "ColorPaletteRegistry". Mismo patrón que <see cref="SimplificationRegistryOptions"/>.
/// </summary>
public sealed class ColorPaletteRegistryOptions
{
    public const string SectionName = "ColorPaletteRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada
    /// <see cref="Vectify.Api.ColorPalette.ColorPaletteVersion"/> (App_Data/color-palettes/{ProjectId}/{ImageId}/{PaletteId}/{Version}.json
    /// por default). Relativa a ContentRootPath salvo que sea una ruta absoluta.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/color-palettes";
}
