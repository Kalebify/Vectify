namespace Vectify.Api.Export;

/// <summary>
/// Resultado de resolver una solicitud de exportación (M1-S10). A diferencia
/// de Vectorization/Simplification/Dimensioning, nunca hay un caso de
/// "recién generado" vs. "cacheado": este servicio NO genera ni persiste
/// ningún artefacto nuevo, solo localiza los bytes YA persistidos por la
/// etapa de origen -- ver spec.md, Definition of Done: "corresponde
/// exactamente a una versión del proyecto".
/// </summary>
public abstract record ExportResult
{
    private ExportResult()
    {
    }

    /// <summary>El SVG de origen existe: listo para que el endpoint lea sus bytes y arme el Content-Disposition.</summary>
    public sealed record Ready(
        string SvgStorageKey,
        string ContentType,
        string FileName,
        ExportSourceKind SourceKind,
        Guid SourceId,
        int Version) : ExportResult;

    /// <summary>No existe un SVG (vectorizado, simplificado o dimensionado) con ese ID para esta imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : ExportResult;

    /// <summary>sourceKind desconocido o sourceId ausente (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : ExportResult;
}
