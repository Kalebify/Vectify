using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Simplification;
using Vectify.Api.Storage;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Checking;

/// <summary>
/// Implementación de <see cref="ICheckService"/>: valida parámetros, localiza
/// el SVG de origen YA generado -- una VectorVersion (M1-S05) vía
/// <see cref="IVectorizationService"/>, o una SimplificationVersion (M1-S07)
/// vía <see cref="ISimplificationService"/>, según request.SourceKind -- y le
/// pide el análisis al motor Python. SIN caché ni lock: a diferencia de
/// Vectorization/Simplification, este servicio nunca persiste nada (es de
/// solo lectura, ver spec.md, Definition of Done), así que no hay una
/// sección crítica de "generar si falta, cachear si existe" que proteger --
/// cada llamada simplemente vuelve a analizar el SVG de origen.
/// </summary>
public sealed class CheckService : ICheckService
{
    private readonly IVectorizationService _vectorizationService;
    private readonly ISimplificationService _simplificationService;
    private readonly ICheckParameterValidator _parameterValidator;
    private readonly IPythonCheckClient _pythonClient;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<CheckService> _logger;

    public CheckService(
        IVectorizationService vectorizationService,
        ISimplificationService simplificationService,
        ICheckParameterValidator parameterValidator,
        IPythonCheckClient pythonClient,
        IFileStorage fileStorage,
        ILogger<CheckService> logger)
    {
        _vectorizationService = vectorizationService;
        _simplificationService = simplificationService;
        _parameterValidator = parameterValidator;
        _pythonClient = pythonClient;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<CheckResult> AnalyzeAsync(Guid projectId, Guid imageId, CheckRequest request, CancellationToken cancellationToken)
    {
        var validation = _parameterValidator.Validate(request);
        if (!validation.IsValid)
        {
            return new CheckResult.ValidationFailed(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var parameters = validation.Parameters!;

        var source = ResolveSource(projectId, imageId, request.SourceId, parameters.SourceKind);
        if (source is null)
        {
            return new CheckResult.NotFound(
                "not_found", "No existe un SVG (vectorizado o simplificado) con ese ID para esta imagen.");
        }

        Stream sourceContent;
        try
        {
            sourceContent = await _fileStorage.OpenReadAsync(source.SvgStorageKey, cancellationToken);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(
                ex,
                "El SVG de origen {SourceId} ({SourceKind}) de {ProjectId}/{ImageId} ya no está disponible en el storage",
                request.SourceId, parameters.SourceKind, projectId, imageId);
            return new CheckResult.UpstreamError(
                "storage_failure", "El SVG de origen ya no está disponible en el storage.");
        }

        PythonCheckResult pythonResult;
        await using (sourceContent)
        {
            pythonResult = await _pythonClient.CheckAsync(
                sourceContent, source.ContentType, "vector.svg", parameters, cancellationToken);
        }

        if (pythonResult.State != PythonCheckState.Success)
        {
            return new CheckResult.UpstreamError(
                MapErrorCode(pythonResult.State),
                pythonResult.Message ?? "No se pudo analizar el SVG.");
        }

        _logger.LogInformation(
            "Análisis de paths completado para {ProjectId}/{ImageId} sobre {SourceKind} {SourceId} " +
            "({IssueCount} issues, {SkippedPathCount} paths omitidos)",
            projectId, imageId, parameters.SourceKind, request.SourceId, pythonResult.Issues!.Count, pythonResult.SkippedPathCount);

        return new CheckResult.Ready(
            request.SourceId, parameters.SourceKind, pythonResult.Issues!, pythonResult.SkippedPathCount!.Value, parameters);
    }

    private CheckSource? ResolveSource(Guid projectId, Guid imageId, Guid sourceId, CheckSourceKind sourceKind)
    {
        switch (sourceKind)
        {
            case CheckSourceKind.Vector:
                var vector = _vectorizationService.FindVector(projectId, imageId, sourceId);
                return vector is null ? null : new CheckSource(vector.SvgStorageKey, vector.ContentType);

            case CheckSourceKind.Simplification:
                var simplification = _simplificationService.FindSimplification(projectId, imageId, sourceId);
                return simplification is null ? null : new CheckSource(simplification.SvgStorageKey, simplification.ContentType);

            default:
                return null;
        }
    }

    private static string MapErrorCode(PythonCheckState state) => state switch
    {
        PythonCheckState.InvalidInputSvg => "invalid_input_svg",
        PythonCheckState.SvgTooLarge => "svg_too_large",
        PythonCheckState.TooManySubpaths => "too_many_subpaths",
        PythonCheckState.InvalidParameters => "invalid_parameters",
        PythonCheckState.Timeout => "timeout",
        PythonCheckState.EngineError => "processing_error",
        PythonCheckState.Unavailable => "engine_unavailable",
        PythonCheckState.InvalidResponse => "invalid_response",
        // Igual que InvalidResponse: la respuesta de Python no pasa la validación defensiva
        // adicional del cliente -- Vectify.Api no confía en su contenido.
        PythonCheckState.InvalidSvg => "invalid_response",
        _ => "processing_error",
    };

    private sealed record CheckSource(string SvgStorageKey, string ContentType);
}
