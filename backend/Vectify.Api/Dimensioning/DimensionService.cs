using System.Collections.Concurrent;
using System.Text;
using Vectify.Api.Contracts;
using Vectify.Api.Simplification;
using Vectify.Api.Storage;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Dimensioning;

/// <summary>
/// Implementación de <see cref="IDimensionService"/>: valida parámetros
/// (en dos pasos, ver <see cref="IDimensionParameterValidator"/>), localiza
/// el SVG de origen YA generado -- una VectorVersion (M1-S05) vía
/// <see cref="IVectorizationService"/>, o una SimplificationVersion (M1-S07)
/// vía <see cref="ISimplificationService"/>, según request.SourceKind -- y
/// reescribe SOLO la metadata del elemento raíz &lt;svg&gt; vía
/// <see cref="SvgDimensionWriter"/> (SIN llamar a ningún motor externo).
/// Mismo patrón de caché+lock+versionado que SimplificationService/
/// VectorizationService (cache-hit crea una versión NUEVA sin retroceder;
/// lock por clave para que requests concurrentes con el mismo SVG de origen +
/// dimensiones no dupliquen la escritura en storage) -- se mantiene por
/// consistencia con el resto del pipeline, aunque acá no hay ninguna llamada
/// costosa a Python que serializar.
/// </summary>
public sealed class DimensionService : IDimensionService
{
    private readonly IVectorizationService _vectorizationService;
    private readonly ISimplificationService _simplificationService;
    private readonly IDimensionVersionRegistry _dimensionRegistry;
    private readonly IDimensionParameterValidator _parameterValidator;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DimensionService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _generationLocks = new();

    public DimensionService(
        IVectorizationService vectorizationService,
        ISimplificationService simplificationService,
        IDimensionVersionRegistry dimensionRegistry,
        IDimensionParameterValidator parameterValidator,
        IFileStorage fileStorage,
        ILogger<DimensionService> logger)
    {
        _vectorizationService = vectorizationService;
        _simplificationService = simplificationService;
        _dimensionRegistry = dimensionRegistry;
        _parameterValidator = parameterValidator;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<DimensionResult> ApplyAsync(
        Guid projectId, Guid imageId, DimensionRequest request, CancellationToken cancellationToken)
    {
        var requestValidation = _parameterValidator.ValidateRequest(request);
        if (!requestValidation.IsValid)
        {
            return new DimensionResult.ValidationFailed(requestValidation.ErrorCode!, requestValidation.ErrorMessage!);
        }

        var raw = requestValidation.Parameters!;

        var source = ResolveSource(projectId, imageId, request.SourceId, raw.SourceKind);
        if (source is null)
        {
            return new DimensionResult.NotFound(
                "not_found", "No existe un SVG (vectorizado o simplificado) con ese ID para esta imagen.");
        }

        var finalValidation = _parameterValidator.ResolveDimensions(raw, source.WidthPx, source.HeightPx);
        if (!finalValidation.IsValid)
        {
            return new DimensionResult.ValidationFailed(finalValidation.ErrorCode!, finalValidation.ErrorMessage!);
        }

        var parameters = finalValidation.Parameters!;

        // Sección crítica: buscar en caché -> generar si falta -> guardar,
        // serializada por (proyecto, imagen, SVG de origen, dimensiones
        // finales) para que requests concurrentes con la misma combinación
        // exacta no dupliquen trabajo -- mismo criterio que SimplificationService.
        var lockKey = $"{projectId:N}/{imageId:N}/{raw.SourceKind}/{request.SourceId:N}/{parameters.ToCacheKey()}";
        var gate = _generationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var cached = _dimensionRegistry.FindByParams(projectId, imageId, request.SourceId, raw.SourceKind, parameters);
            if (cached is not null)
            {
                var cachedVersion = _dimensionRegistry.NextVersion(projectId, imageId);
                var cachedRecord = cached with
                {
                    Version = cachedVersion,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                _dimensionRegistry.Save(cachedRecord);

                _logger.LogInformation(
                    "Dimensión {DimensionId} (v{Version}) reutilizada desde caché para {ProjectId}/{ImageId} ({WidthMm}mm x {HeightMm}mm)",
                    cachedRecord.DimensionId, cachedVersion, projectId, imageId, parameters.WidthMm, parameters.HeightMm);

                return new DimensionResult.Ready(cachedRecord, FromCache: true);
            }

            string svgText;
            try
            {
                await using var sourceContent = await _fileStorage.OpenReadAsync(source.SvgStorageKey, cancellationToken);
                using var reader = new StreamReader(sourceContent, Encoding.UTF8);
                svgText = await reader.ReadToEndAsync(cancellationToken);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(
                    ex,
                    "El SVG de origen {SourceId} ({SourceKind}) de {ProjectId}/{ImageId} ya no está disponible en el storage",
                    request.SourceId, raw.SourceKind, projectId, imageId);
                return new DimensionResult.UpstreamError(
                    "storage_failure", "El SVG de origen ya no está disponible en el storage.");
            }

            string dimensionedSvg;
            try
            {
                dimensionedSvg = SvgDimensionWriter.Apply(
                    svgText, source.WidthPx, source.HeightPx, parameters.WidthMm, parameters.HeightMm, deform: !parameters.LockAspectRatio);
            }
            catch (InvalidDimensionSourceSvgException ex)
            {
                _logger.LogError(
                    ex,
                    "El SVG de origen {SourceId} ({SourceKind}) de {ProjectId}/{ImageId} no se pudo reescribir con dimensiones físicas",
                    request.SourceId, raw.SourceKind, projectId, imageId);
                return new DimensionResult.UpstreamError("invalid_source_svg", ex.Message);
            }

            var dimensionId = Guid.NewGuid();
            var storageKey = $"{projectId:N}/{imageId:N}/dimensions/{dimensionId:N}.svg";

            try
            {
                var svgBytes = Encoding.UTF8.GetBytes(dimensionedSvg);
                await using var svgContent = new MemoryStream(svgBytes);
                await _fileStorage.SaveAsync(storageKey, svgContent, source.ContentType, cancellationToken);
            }
            catch (FileStorageException ex)
            {
                _logger.LogError(ex, "Fallo de storage al guardar el SVG con dimensiones físicas {ProjectId}/{ImageId}", projectId, imageId);
                return new DimensionResult.UpstreamError(
                    "storage_failure", "No se pudo guardar el SVG con dimensiones físicas.");
            }

            var version = _dimensionRegistry.NextVersion(projectId, imageId);
            var record = new DimensionVersion(
                projectId,
                imageId,
                version,
                dimensionId,
                request.SourceId,
                raw.SourceKind,
                parameters,
                storageKey,
                source.ContentType,
                source.WidthPx,
                source.HeightPx,
                DateTimeOffset.UtcNow);

            _dimensionRegistry.Save(record);

            _logger.LogInformation(
                "Dimensión {DimensionId} (v{Version}) generada para {ProjectId}/{ImageId}: {WidthMm}mm x {HeightMm}mm (lock={LockAspectRatio})",
                dimensionId, version, projectId, imageId, parameters.WidthMm, parameters.HeightMm, parameters.LockAspectRatio);

            return new DimensionResult.Ready(record, FromCache: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public DimensionVersion? FindDimension(Guid projectId, Guid imageId, Guid dimensionId) =>
        _dimensionRegistry.FindByDimensionId(projectId, imageId, dimensionId);

    private DimensionSource? ResolveSource(Guid projectId, Guid imageId, Guid sourceId, DimensionSourceKind sourceKind)
    {
        switch (sourceKind)
        {
            case DimensionSourceKind.Vector:
                var vector = _vectorizationService.FindVector(projectId, imageId, sourceId);
                return vector is null ? null : new DimensionSource(vector.SvgStorageKey, vector.ContentType, vector.Width, vector.Height);

            case DimensionSourceKind.Simplification:
                var simplification = _simplificationService.FindSimplification(projectId, imageId, sourceId);
                return simplification is null
                    ? null
                    : new DimensionSource(simplification.SvgStorageKey, simplification.ContentType, simplification.Width, simplification.Height);

            default:
                return null;
        }
    }

    private sealed record DimensionSource(string SvgStorageKey, string ContentType, int WidthPx, int HeightPx);
}
