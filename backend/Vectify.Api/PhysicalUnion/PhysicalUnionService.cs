using System.Collections.Concurrent;
using System.Text;
using Vectify.Api.Clients;
using Vectify.Api.Components;
using Vectify.Api.Contracts;
using Vectify.Api.Storage;
using Vectify.Api.Vectorization;

namespace Vectify.Api.PhysicalUnion;

/// <summary>
/// Implementación de <see cref="IPhysicalUnionService"/>. Preview y confirm
/// comparten EXACTAMENTE el mismo cómputo (<see cref="ComputeAsync"/> --
/// localizar el VectorId + la ComponentSetVersion vigente, validar la
/// selección, llamar a Python para la unión geométrica real): la única
/// diferencia es que confirm, y SOLO confirm, persiste el resultado si
/// `ComputeAsync` tuvo éxito -- preview SIEMPRE es de solo lectura, sin
/// importar el resultado (ver spec.md, criterio de aceptación: "la
/// operación no se persiste hasta que el usuario confirma explícitamente").
/// Lock por VectorId de origen en confirm (mismo criterio que
/// ComponentGroupService) para que dos confirmaciones concurrentes sobre la
/// misma capa no pisen el avance de versión una de la otra.
/// </summary>
public sealed class PhysicalUnionService : IPhysicalUnionService
{
    private readonly IComponentAnalysisService _componentAnalysisService;
    private readonly IVectorizationService _vectorizationService;
    private readonly IVectorVersionRegistry _vectorRegistry;
    private readonly IPhysicalUnionVersionRegistry _unionRegistry;
    private readonly IPythonPhysicalUnionClient _pythonClient;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<PhysicalUnionService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _vectorLocks = new();

    public PhysicalUnionService(
        IComponentAnalysisService componentAnalysisService,
        IVectorizationService vectorizationService,
        IVectorVersionRegistry vectorRegistry,
        IPhysicalUnionVersionRegistry unionRegistry,
        IPythonPhysicalUnionClient pythonClient,
        IFileStorage fileStorage,
        ILogger<PhysicalUnionService> logger)
    {
        _componentAnalysisService = componentAnalysisService;
        _vectorizationService = vectorizationService;
        _vectorRegistry = vectorRegistry;
        _unionRegistry = unionRegistry;
        _pythonClient = pythonClient;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<PhysicalUnionPreviewResult> PreviewAsync(
        Guid projectId, Guid imageId, Guid vectorId, PhysicalUnionRequest request, CancellationToken cancellationToken)
    {
        var computation = await ComputeAsync(projectId, imageId, vectorId, request, cancellationToken);
        return computation switch
        {
            _ComputationResult.Success success => new PhysicalUnionPreviewResult.Ready(success.Outcome),
            _ComputationResult.Failure failure => failure.Kind switch
            {
                _FailureKind.NotFound => new PhysicalUnionPreviewResult.NotFound(failure.Code, failure.Message),
                _FailureKind.ValidationFailed => new PhysicalUnionPreviewResult.ValidationFailed(failure.Code, failure.Message),
                _FailureKind.GeometryImpossible => new PhysicalUnionPreviewResult.GeometryImpossible(failure.Code, failure.Message),
                _ => new PhysicalUnionPreviewResult.UpstreamError(failure.Code, failure.Message),
            },
            _ => new PhysicalUnionPreviewResult.UpstreamError("internal_error", "Ocurrió un error inesperado al calcular el preview de la unión física."),
        };
    }

    public async Task<PhysicalUnionConfirmResult> ConfirmAsync(
        Guid projectId, Guid imageId, Guid vectorId, PhysicalUnionRequest request, CancellationToken cancellationToken)
    {
        var gate = VectorGate(projectId, imageId, vectorId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var computation = await ComputeAsync(projectId, imageId, vectorId, request, cancellationToken);
            if (computation is _ComputationResult.Failure failure)
            {
                return failure.Kind switch
                {
                    _FailureKind.NotFound => new PhysicalUnionConfirmResult.NotFound(failure.Code, failure.Message),
                    _FailureKind.ValidationFailed => new PhysicalUnionConfirmResult.ValidationFailed(failure.Code, failure.Message),
                    _FailureKind.GeometryImpossible => new PhysicalUnionConfirmResult.GeometryImpossible(failure.Code, failure.Message),
                    _ => new PhysicalUnionConfirmResult.UpstreamError(failure.Code, failure.Message),
                };
            }

            var success = (_ComputationResult.Success)computation;
            var outcome = success.Outcome;
            var distinctIds = success.DistinctComponentIds;

            var newVectorId = Guid.NewGuid();
            var storageKey = $"{projectId:N}/{imageId:N}/vectors/{newVectorId:N}.svg";

            try
            {
                var svgBytes = Encoding.UTF8.GetBytes(outcome.Svg);
                await using var svgContent = new MemoryStream(svgBytes);
                await _fileStorage.SaveAsync(storageKey, svgContent, outcome.ContentType, cancellationToken);
            }
            catch (FileStorageException ex)
            {
                _logger.LogError(
                    ex, "Fallo de storage al guardar el SVG fusionado para {ProjectId}/{ImageId}/{VectorId}",
                    projectId, imageId, vectorId);
                return new PhysicalUnionConfirmResult.UpstreamError(
                    "storage_failure", "No se pudo guardar el SVG resultante de la unión física.");
            }

            var vectorVersionNumber = _vectorRegistry.NextVersion(projectId, imageId);
            var newVector = new VectorVersion(
                projectId,
                imageId,
                vectorVersionNumber,
                newVectorId,
                success.SourceVector.SourceMaskId,
                success.SourceVector.Parameters,
                storageKey,
                outcome.ContentType,
                outcome.Width,
                outcome.Height,
                outcome.Metrics,
                DateTimeOffset.UtcNow);
            _vectorRegistry.Save(newVector);

            var unionVersionNumber = _unionRegistry.NextVersion(projectId, imageId, vectorId);
            var record = new PhysicalUnionVersion(
                projectId,
                imageId,
                unionVersionNumber,
                vectorId,
                distinctIds,
                newVectorId,
                outcome.ComponentCountBefore,
                outcome.ComponentCountAfter,
                outcome.Strategy,
                outcome.BridgeCount,
                DateTimeOffset.UtcNow);
            _unionRegistry.Save(record);

            _logger.LogInformation(
                "Unión física confirmada: {ComponentCount} componentes de {SourceVectorId} fusionados en el nuevo vector " +
                "{NewVectorId} (v{VectorVersion}) de {ProjectId}/{ImageId} (estrategia={Strategy}, bridges={BridgeCount}); " +
                "la versión previa (SourceVectorId de arriba) NO se destruye.",
                distinctIds.Count, vectorId, newVectorId, vectorVersionNumber, projectId, imageId, outcome.Strategy, outcome.BridgeCount);

            return new PhysicalUnionConfirmResult.Ready(newVector, record);
        }
        finally
        {
            gate.Release();
        }
    }

    public PhysicalUnionVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId) =>
        _unionRegistry.FindLatest(projectId, imageId, vectorId);

    /// <summary>
    /// Cómputo COMPLETO compartido por preview/confirm, sin persistir NADA
    /// (ni siquiera en confirm -- eso lo hace <see cref="ConfirmAsync"/>
    /// DESPUÉS de recibir un <see cref="_ComputationResult.Success"/>):
    /// valida la selección contra la ComponentSetVersion vigente del
    /// VectorId, localiza el SVG de origen y le pide a Python la unión
    /// geométrica real (booleanas/bridging + la validación post-operación
    /// no negociable, ya aplicada del lado de Python -- ver
    /// app.core.physical_union).
    /// </summary>
    private async Task<_ComputationResult> ComputeAsync(
        Guid projectId, Guid imageId, Guid vectorId, PhysicalUnionRequest request, CancellationToken cancellationToken)
    {
        var distinctIds = (request.ComponentIds ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (distinctIds.Count < 2)
        {
            return _ComputationResult.Fail(_FailureKind.ValidationFailed, "invalid_parameters", "Seleccioná al menos 2 componentes distintos para unir físicamente.");
        }

        var componentSet = _componentAnalysisService.FindLatest(projectId, imageId, vectorId);
        if (componentSet is null)
        {
            return _ComputationResult.Fail(_FailureKind.NotFound, "not_found", "No existe un análisis de componentes (M2-S03) calculado para ese vector.");
        }

        var componentsById = componentSet.Components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var missingIds = distinctIds.Where(id => !componentsById.ContainsKey(id)).ToList();
        if (missingIds.Count > 0)
        {
            return _ComputationResult.Fail(
                _FailureKind.NotFound,
                "component_not_found",
                $"Uno o más componentes indicados no existen en la versión vigente de componentes de ese vector: {string.Join(", ", missingIds)}.");
        }

        var vector = _vectorizationService.FindVector(projectId, imageId, vectorId);
        if (vector is null)
        {
            return _ComputationResult.Fail(_FailureKind.NotFound, "not_found", "No existe una capa vectorial (VectorVersion) con ese ID para esta imagen.");
        }

        // Orden estable = orden en que el usuario los seleccionó (irrelevante
        // para la corrección del resultado -- la unión/bridging es
        // simétrica -- pero mantiene mensajes de error y el árbol de
        // expansión mínima deterministas entre llamadas idénticas).
        var selectedComponents = distinctIds.Select(id => componentsById[id]).ToList();

        Stream sourceContent;
        try
        {
            sourceContent = await _fileStorage.OpenReadAsync(vector.SvgStorageKey, cancellationToken);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(
                ex, "El SVG de la capa (vector {VectorId}) de {ProjectId}/{ImageId} ya no está disponible en el storage",
                vectorId, projectId, imageId);
            return _ComputationResult.Fail(_FailureKind.UpstreamError, "storage_failure", "El SVG de la capa ya no está disponible en el storage.");
        }

        PythonPhysicalUnionResult pythonResult;
        await using (sourceContent)
        {
            pythonResult = await _pythonClient.UnionAsync(sourceContent, vector.ContentType, "layer.svg", selectedComponents, cancellationToken);
        }

        if (pythonResult.State != PythonPhysicalUnionState.Success)
        {
            var (kind, code) = MapError(pythonResult.State);
            return _ComputationResult.Fail(kind, code, pythonResult.Message ?? "No se pudo unir físicamente las piezas seleccionadas.");
        }

        var outcome = new PhysicalUnionOutcome(
            pythonResult.Svg!,
            pythonResult.ContentType!,
            pythonResult.Width!.Value,
            pythonResult.Height!.Value,
            pythonResult.Metrics!,
            pythonResult.ComponentCountBefore!.Value,
            pythonResult.ComponentCountAfter!.Value,
            pythonResult.Strategy!,
            pythonResult.BridgeCount!.Value);

        return _ComputationResult.Succeed(outcome, vector, distinctIds);
    }

    private static (_FailureKind Kind, string Code) MapError(PythonPhysicalUnionState state) => state switch
    {
        PythonPhysicalUnionState.InvalidInputSvg => (_FailureKind.UpstreamError, "invalid_input_svg"),
        PythonPhysicalUnionState.SvgTooLarge => (_FailureKind.UpstreamError, "svg_too_large"),
        PythonPhysicalUnionState.InvalidParameters => (_FailureKind.ValidationFailed, "invalid_parameters"),
        PythonPhysicalUnionState.InvalidGeometry => (_FailureKind.GeometryImpossible, "physical_union_invalid_geometry"),
        PythonPhysicalUnionState.Impossible => (_FailureKind.GeometryImpossible, "physical_union_impossible"),
        PythonPhysicalUnionState.Timeout => (_FailureKind.UpstreamError, "timeout"),
        PythonPhysicalUnionState.Unavailable => (_FailureKind.UpstreamError, "engine_unavailable"),
        PythonPhysicalUnionState.InvalidResponse => (_FailureKind.UpstreamError, "invalid_response"),
        _ => (_FailureKind.UpstreamError, "processing_error"),
    };

    private static SemaphoreSlim VectorGate(Guid projectId, Guid imageId, Guid vectorId) =>
        _vectorLocks.GetOrAdd($"{projectId:N}/{imageId:N}/{vectorId:N}", _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// "Either" privado mínimo para compartir `ComputeAsync` entre preview y
    /// confirm sin repetir la orquestación completa dos veces -- C# no tiene
    /// un tipo union liviano nativo, así que se modela a mano con dos casos
    /// sealed, igual espíritu que ComponentGroupResult/ComponentSetResult
    /// pero interno a este servicio (nunca cruza su límite público).
    /// </summary>
    private abstract record _ComputationResult
    {
        public static _ComputationResult Succeed(PhysicalUnionOutcome outcome, VectorVersion sourceVector, List<string> distinctComponentIds) =>
            new Success(outcome, sourceVector, distinctComponentIds);

        public static _ComputationResult Fail(_FailureKind kind, string code, string message) =>
            new Failure(kind, code, message);

        public sealed record Success(PhysicalUnionOutcome Outcome, VectorVersion SourceVector, List<string> DistinctComponentIds) : _ComputationResult;

        public sealed record Failure(_FailureKind Kind, string Code, string Message) : _ComputationResult;
    }

    private enum _FailureKind
    {
        NotFound,
        ValidationFailed,
        GeometryImpossible,
        UpstreamError,
    }
}
