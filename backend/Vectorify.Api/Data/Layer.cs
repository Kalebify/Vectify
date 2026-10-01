using Vectorify.Api.ManufacturingOperations;

namespace Vectorify.Api.Data;

/// <summary>
/// Una capa persistente dentro de una <see cref="DocumentVersion"/> (M2.2-S02) -- unifica
/// conceptualmente dos sidecars de archivos hoy separados
/// (<see cref="Vectorify.Api.LayerLayout.LayerLayoutSetVersion"/> para Order/Visible/Locked/
/// Name y <see cref="Vectorify.Api.ManufacturingOperations.ManufacturingOperationAssignment"/>
/// para la operación de fabricación) en una sola tabla relacional. La MIGRACIÓN real de esos
/// datos existentes no es parte de esta tarjeta -- ambos sidecars siguen funcionando sin
/// cambios.
///
/// <see cref="ManufacturingOperation"/> reutiliza el enum YA EXISTENTE
/// <see cref="ManufacturingOperationKind"/> (nunca un tipo paralelo), persistido como texto
/// (<c>HasConversion&lt;string&gt;()</c>). <c>null</c> significa "sin asignar", mismo
/// criterio que la ausencia de un <see cref="ManufacturingOperationAssignment"/> hoy (nunca
/// asumir "Corte" por defecto silenciosamente).
/// </summary>
public sealed class Layer
{
    public Guid Id { get; set; }

    public Guid VersionId { get; set; }

    public DocumentVersion? Version { get; set; }

    public Guid ColorId { get; set; }

    public PaletteColor? Color { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Order { get; set; }

    public bool Visible { get; set; }

    public bool Locked { get; set; }

    public ManufacturingOperationKind? ManufacturingOperation { get; set; }
}
