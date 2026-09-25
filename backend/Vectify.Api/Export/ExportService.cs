using Vectify.Api.Dimensioning;
using Vectify.Api.Projects;
using Vectify.Api.Simplification;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Export;

/// <summary>
/// Implementación de <see cref="IExportService"/>: valida sourceKind/
/// sourceId, localiza el SVG de origen YA generado (Vector/Simplification/
/// Dimension, ver <see cref="ExportSourceKind"/>) y deriva el nombre de
/// archivo de descarga a partir de <c>ProjectRecord.FileName</c> (el nombre
/// original que el usuario subió en M1-S02) vía
/// <see cref="ExportFileNameSanitizer"/>. "Auditoría básica de versión
/// exportada" (spec.md): logging estructurado de cada export resuelto con
/// éxito -- qué versión, de qué proyecto/imagen, cuándo (el timestamp lo
/// agrega el propio logger). No hay ningún registro persistido adicional:
/// ver decisión documentada en el reporte del sprint (mismo criterio YAGNI ya
/// aplicado en Checking, M1-S08, que tampoco persiste sus análisis).
/// </summary>
public sealed class ExportService : IExportService
{
    private readonly IVectorizationService _vectorizationService;
    private readonly ISimplificationService _simplificationService;
    private readonly IDimensionService _dimensionService;
    private readonly IProjectRegistry _projectRegistry;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        IVectorizationService vectorizationService,
        ISimplificationService simplificationService,
        IDimensionService dimensionService,
        IProjectRegistry projectRegistry,
        ILogger<ExportService> logger)
    {
        _vectorizationService = vectorizationService;
        _simplificationService = simplificationService;
        _dimensionService = dimensionService;
        _projectRegistry = projectRegistry;
        _logger = logger;
    }

    public ExportResult Resolve(Guid projectId, Guid imageId, string? sourceKind, Guid sourceId)
    {
        if (sourceId == Guid.Empty)
        {
            return new ExportResult.ValidationFailed(
                "invalid_parameters", "El sourceId del SVG a exportar es requerido.");
        }

        var parsedKind = ParseSourceKind(sourceKind);
        if (parsedKind is null)
        {
            return new ExportResult.ValidationFailed(
                "invalid_parameters",
                $"sourceKind '{sourceKind}' desconocido. Valores válidos: vector, simplification, dimension.");
        }

        var source = ResolveSource(projectId, imageId, sourceId, parsedKind.Value);
        if (source is null)
        {
            return new ExportResult.NotFound(
                "not_found",
                "No existe un SVG (vectorizado, simplificado o dimensionado) con ese ID para esta imagen.");
        }

        // El nombre original es solo un insumo de UX (nombre de descarga
        // sugerido) -- si el proyecto ya no está en el registro (caso límite,
        // no debería ocurrir en la práctica: el SVG de origen no existiría sin
        // un proyecto), ExportFileNameSanitizer cae al nombre por defecto en
        // vez de fallar la exportación.
        var project = _projectRegistry.Find(projectId, imageId);
        var stageSuffix = $"{SourceKindToWireValue(parsedKind.Value)}-v{source.Version}";
        var fileName = ExportFileNameSanitizer.Build(project?.FileName, stageSuffix);

        _logger.LogInformation(
            "Export de SVG: {SourceKind} {SourceId} (v{Version}) de {ProjectId}/{ImageId} como '{FileName}'",
            parsedKind.Value, sourceId, source.Version, projectId, imageId, fileName);

        return new ExportResult.Ready(
            source.SvgStorageKey, source.ContentType, fileName, parsedKind.Value, sourceId, source.Version);
    }

    private ExportSource? ResolveSource(Guid projectId, Guid imageId, Guid sourceId, ExportSourceKind sourceKind)
    {
        switch (sourceKind)
        {
            case ExportSourceKind.Vector:
                var vector = _vectorizationService.FindVector(projectId, imageId, sourceId);
                return vector is null ? null : new ExportSource(vector.SvgStorageKey, vector.ContentType, vector.Version);

            case ExportSourceKind.Simplification:
                var simplification = _simplificationService.FindSimplification(projectId, imageId, sourceId);
                return simplification is null
                    ? null
                    : new ExportSource(simplification.SvgStorageKey, simplification.ContentType, simplification.Version);

            case ExportSourceKind.Dimension:
                var dimension = _dimensionService.FindDimension(projectId, imageId, sourceId);
                return dimension is null
                    ? null
                    : new ExportSource(dimension.SvgStorageKey, dimension.ContentType, dimension.Version);

            default:
                return null;
        }
    }

    private static ExportSourceKind? ParseSourceKind(string? sourceKind)
    {
        if (string.IsNullOrWhiteSpace(sourceKind))
        {
            return null;
        }

        return sourceKind.Trim().ToLowerInvariant() switch
        {
            "vector" => ExportSourceKind.Vector,
            "simplification" => ExportSourceKind.Simplification,
            "dimension" => ExportSourceKind.Dimension,
            _ => null,
        };
    }

    private static string SourceKindToWireValue(ExportSourceKind sourceKind) => sourceKind switch
    {
        ExportSourceKind.Vector => "vector",
        ExportSourceKind.Simplification => "simplification",
        ExportSourceKind.Dimension => "dimension",
        _ => "export",
    };

    private sealed record ExportSource(string SvgStorageKey, string ContentType, int Version);
}
