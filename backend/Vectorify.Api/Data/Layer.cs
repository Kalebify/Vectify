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
///
/// <see cref="SvgAssetId"/> (M2.2-S05, migración aditiva <c>AddLayerSvgAsset</c>): el SVG ya
/// coloreado de esta capa, subido como <see cref="Asset"/> propio (<c>type: "layer-svg"</c>)
/// en vez de inventar un formato de SVG compuesto con un <c>&lt;g&gt;</c> por layer -- ver el
/// ADR en spec.md M2.2-S05, "Geometría: qué se normaliza en tablas vs. qué se guarda como
/// asset". Nullable: un Layer puede existir sin snapshot SVG todavía (defensivo; en la
/// práctica el flujo de Save siempre lo sube antes de escribir esta fila). FK a Asset con
/// <c>OnDelete: SetNull</c> -- borrar el Asset no debe cascadear el borrado del Layer, es
/// metadata recuperable con un nuevo Save.
///
/// <see cref="PathCount"/> (M2.2-S05, migración aditiva <c>AddLayerPathCount</c>, agregada tras
/// revisión del orquestador -- no estaba en spec.md a nivel de código): copia de
/// <c>VectorVersion.Metrics.PathCount</c> (ya resuelto en <c>VectorDocumentService.SaveAsync</c>
/// para leer el SVG de la capa) en el momento del Save. Sin esto, un documento reabierto
/// (<c>GET .../document</c>) no tiene forma de recuperar ese número -- el bug real encontrado:
/// el frontend mostraba "0" en el panel Inspector (en vez de "—"/el valor real) y
/// "Seleccionar todo en la capa" quedaba silenciosamente roto (haría un loop
/// <c>0..pathCount-1</c> de 0 iteraciones) para cualquier proyecto reabierto.
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

    public Guid? SvgAssetId { get; set; }

    public Asset? SvgAsset { get; set; }

    public int PathCount { get; set; }
}
