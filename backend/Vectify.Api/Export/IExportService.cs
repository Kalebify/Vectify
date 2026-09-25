namespace Vectify.Api.Export;

/// <summary>
/// Orquesta la exportación SVG (M1-S10): valida sourceKind/sourceId, localiza
/// el SVG YA generado -- una VectorVersion (M1-S05) vía
/// <see cref="Vectify.Api.Vectorization.IVectorizationService"/>, una
/// SimplificationVersion (M1-S07) vía
/// <see cref="Vectify.Api.Simplification.ISimplificationService"/>, o una
/// DimensionVersion (M1-S09) vía
/// <see cref="Vectify.Api.Dimensioning.IDimensionService"/>, según
/// sourceKind -- y deriva un nombre de archivo de descarga sanitizado. SIN
/// caché/lock/registro versionado propio (a diferencia de las etapas que sí
/// generan un artefacto nuevo): este servicio nunca genera ni persiste
/// geometría, solo REFERENCIA bytes ya existentes -- ver spec.md, Definition
/// of Done: "corresponde exactamente a una versión del proyecto". Es
/// responsabilidad del caller (Endpoints.ExportEndpoints) abrir el stream de
/// storage y armar la respuesta HTTP con los headers correctos.
/// </summary>
public interface IExportService
{
    ExportResult Resolve(Guid projectId, Guid imageId, string? sourceKind, Guid sourceId);
}
