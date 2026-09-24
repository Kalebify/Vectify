using System.Collections.Concurrent;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Projects;
using Vectify.Api.Storage;

namespace Vectify.Api.Preprocessing;

/// <summary>
/// Implementación de <see cref="IPreprocessService"/>: valida parámetros, busca
/// un preview cacheado para esos parámetros exactos y, si no existe, orquesta la
/// llamada a Python sobre el original ya guardado (que nunca se modifica: se
/// abre en modo lectura y el resultado se guarda bajo una clave nueva), versiona
/// la configuración y la registra.
/// </summary>
public sealed class PreprocessService : IPreprocessService
{
    private readonly IProjectRegistry _projectRegistry;
    private readonly IPreprocessConfigRegistry _preprocessRegistry;
    private readonly IPreprocessParameterValidator _parameterValidator;
    private readonly IPythonPreprocessClient _pythonClient;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<PreprocessService> _logger;

    // Lock asíncrono por clave (proyecto, imagen, parámetros efectivos): evita
    // que dos requests concurrentes con exactamente los mismos parámetros
    // pasen ambas el chequeo de caché como "miss" y dupliquen la llamada a
    // Python (ver spec.md M1-S03: "cachear/referenciar preview"). Los
    // semáforos quedan vivos en este diccionario para todo el ciclo de vida
    // del proceso (no se limpian) — aceptable para este sprint, sin base de
    // datos ni infra distribuida detrás.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _generationLocks = new();

    public PreprocessService(
        IProjectRegistry projectRegistry,
        IPreprocessConfigRegistry preprocessRegistry,
        IPreprocessParameterValidator parameterValidator,
        IPythonPreprocessClient pythonClient,
        IFileStorage fileStorage,
        ILogger<PreprocessService> logger)
    {
        _projectRegistry = projectRegistry;
        _preprocessRegistry = preprocessRegistry;
        _parameterValidator = parameterValidator;
        _pythonClient = pythonClient;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<PreprocessResult> GeneratePreviewAsync(
        Guid projectId, Guid imageId, PreprocessRequest request, CancellationToken cancellationToken)
    {
        var original = _projectRegistry.Find(projectId, imageId);
        if (original is null)
        {
            return new PreprocessResult.NotFound();
        }

        var validation = _parameterValidator.Validate(request);
        if (!validation.IsValid)
        {
            return new PreprocessResult.ValidationFailed(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var parameters = validation.Parameters!;

        // Sección crítica: buscar en caché -> generar si falta -> guardar,
        // serializada por (proyecto, imagen, parámetros) para que requests
        // concurrentes con la misma combinación exacta no dupliquen trabajo
        // (ver defecto de concurrencia corregido en esta ronda).
        var lockKey = $"{projectId:N}/{imageId:N}/{parameters.ToCacheKey()}";
        var gate = _generationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Cache: mismos parámetros efectivos para esta imagen ya generaron
            // un preview antes -> se reutilizan los bytes/archivo ya generados
            // (no se vuelve a llamar a Python ni a guardar en storage), pero se
            // registra igual como una versión NUEVA del historial, para que un
            // reset a parámetros ya vistos avance la versión en vez de
            // retroceder a la vieja (spec.md: "cachear/referenciar preview;
            // mantener versionado de configuración").
            var cached = _preprocessRegistry.FindByParams(projectId, imageId, parameters);
            if (cached is not null)
            {
                var cachedVersion = _preprocessRegistry.NextVersion(projectId, imageId);
                var cachedRecord = cached with
                {
                    Version = cachedVersion,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                _preprocessRegistry.Save(cachedRecord);

                _logger.LogInformation(
                    "Preview {PreviewId} (v{Version}) reutilizado desde caché para {ProjectId}/{ImageId}",
                    cachedRecord.PreviewId, cachedVersion, projectId, imageId);

                return new PreprocessResult.Ready(cachedRecord, FromCache: true);
            }

            Stream originalContent;
            try
            {
                originalContent = await _fileStorage.OpenReadAsync(original.StorageKey, cancellationToken);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "El original de {ProjectId}/{ImageId} ya no está disponible en el storage", projectId, imageId);
                return new PreprocessResult.UpstreamError(
                    "storage_failure", "El original ya no está disponible en el storage.");
            }

            PythonPreprocessResult pythonResult;
            await using (originalContent)
            {
                pythonResult = await _pythonClient.PreprocessAsync(
                    originalContent, original.MimeType, original.FileName, parameters, cancellationToken);
            }

            if (pythonResult.State != PythonPreprocessState.Success)
            {
                return new PreprocessResult.UpstreamError(
                    MapErrorCode(pythonResult.State),
                    pythonResult.Message ?? "No se pudo generar el preview.");
            }

            var previewId = Guid.NewGuid();
            var storageKey = $"{projectId:N}/{imageId:N}/previews/{previewId:N}.png";

            try
            {
                await using var previewContent = new MemoryStream(pythonResult.ImageBytes!);
                await _fileStorage.SaveAsync(storageKey, previewContent, pythonResult.ContentType!, cancellationToken);
            }
            catch (FileStorageException ex)
            {
                _logger.LogError(ex, "Fallo de storage al guardar el preview {ProjectId}/{ImageId}", projectId, imageId);
                return new PreprocessResult.UpstreamError(
                    "storage_failure", "No se pudo guardar el preview generado.");
            }

            var version = _preprocessRegistry.NextVersion(projectId, imageId);
            var record = new PreprocessConfigRecord(
                projectId,
                imageId,
                version,
                previewId,
                pythonResult.EffectiveParameters ?? parameters,
                storageKey,
                pythonResult.ContentType!,
                pythonResult.Width!.Value,
                pythonResult.Height!.Value,
                pythonResult.OriginalWidth!.Value,
                pythonResult.OriginalHeight!.Value,
                pythonResult.Metrics!,
                DateTimeOffset.UtcNow);

            _preprocessRegistry.Save(record);

            _logger.LogInformation(
                "Preview {PreviewId} (v{Version}) generado para {ProjectId}/{ImageId}",
                previewId, version, projectId, imageId);

            return new PreprocessResult.Ready(record, FromCache: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public PreprocessConfigRecord? FindPreview(Guid projectId, Guid imageId, Guid previewId) =>
        _preprocessRegistry.FindByPreviewId(projectId, imageId, previewId);

    private static string MapErrorCode(PythonPreprocessState state) => state switch
    {
        PythonPreprocessState.CorruptImage => "corrupt_file",
        PythonPreprocessState.DimensionsExceeded => "dimensions_exceeded",
        PythonPreprocessState.InvalidParameters => "invalid_parameters",
        PythonPreprocessState.Timeout => "timeout",
        PythonPreprocessState.Unavailable => "engine_unavailable",
        PythonPreprocessState.InvalidResponse => "invalid_response",
        _ => "processing_error",
    };
}
