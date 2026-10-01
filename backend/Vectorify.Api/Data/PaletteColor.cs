namespace Vectorify.Api.Data;

/// <summary>
/// Un color de paleta persistente dentro de una <see cref="DocumentVersion"/> (M2.2-S02) --
/// equivalente persistente de <see cref="Vectorify.Api.ColorPalette.ColorGroup"/>
/// (<see cref="Hex"/>≈ColorHex, <see cref="Coverage"/>≈AreaPercent,
/// <see cref="IsBackground"/>≈IsExcluded). Deliberadamente NO descompone <see cref="Hex"/>
/// en columnas R/G/B separadas: mismo criterio que <c>ColorGroup.ColorHex</c> (que tampoco
/// lo hace) -- R/G/B serían datos derivados redundantes del mismo Hex, sin un caso de uso
/// de query que los necesite como columnas propias todavía. Ver ADR en IMPL.md.
/// </summary>
public sealed class PaletteColor
{
    public Guid Id { get; set; }

    public Guid VersionId { get; set; }

    public DocumentVersion? Version { get; set; }

    public string Hex { get; set; } = string.Empty;

    public double Coverage { get; set; }

    public bool IsBackground { get; set; }

    public int Order { get; set; }

    public ICollection<Layer> Layers { get; set; } = new List<Layer>();
}
