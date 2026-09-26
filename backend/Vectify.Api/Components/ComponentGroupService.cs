using System.Collections.Concurrent;
using Vectify.Api.Contracts;

namespace Vectify.Api.Components;

/// <summary>
/// Implementación de <see cref="IComponentGroupService"/>: agrupar valida
/// los componentIds seleccionados contra la ÚLTIMA
/// <see cref="ComponentSetVersion"/> vigente de ese VectorId (vía
/// <see cref="IComponentAnalysisService.FindLatest"/> -- nunca llama a
/// Python ni dispara un cálculo nuevo); desagrupar y renombrar son ediciones
/// de metadata puras sobre la última versión del conjunto de grupos, SIN
/// volver a validar contra componentes (un grupo, una vez creado, sigue
/// siendo válido para la versión de componentes con la que se creó -- ver
/// spec.md, "Ambigüedades detectadas": no se migra automáticamente un grupo
/// viejo a una versión de componentes recalculada). Lock por VectorId para
/// que ediciones concurrentes sobre el mismo conjunto de grupos no pisen el
/// avance de versión una de la otra -- mismo criterio que
/// ColorPaletteService (lock por sesión) aplicado acá a nivel de VectorId,
/// que es la única "sesión" de agrupación posible por capa.
/// </summary>
public sealed class ComponentGroupService : IComponentGroupService
{
    private readonly IComponentAnalysisService _componentAnalysisService;
    private readonly IComponentGroupVersionRegistry _groupRegistry;
    private readonly ILogger<ComponentGroupService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _vectorLocks = new();

    public ComponentGroupService(
        IComponentAnalysisService componentAnalysisService,
        IComponentGroupVersionRegistry groupRegistry,
        ILogger<ComponentGroupService> logger)
    {
        _componentAnalysisService = componentAnalysisService;
        _groupRegistry = groupRegistry;
        _logger = logger;
    }

    public async Task<ComponentGroupResult> GroupAsync(
        Guid projectId, Guid imageId, Guid vectorId, ComponentGroupCreateRequest request, CancellationToken cancellationToken)
    {
        var distinctIds = (request.ComponentIds ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (distinctIds.Count < 2)
        {
            return new ComponentGroupResult.ValidationFailed(
                "invalid_parameters", "Seleccioná al menos 2 componentes distintos para agrupar.");
        }

        var gate = VectorGate(projectId, imageId, vectorId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var componentSet = _componentAnalysisService.FindLatest(projectId, imageId, vectorId);
            if (componentSet is null)
            {
                return new ComponentGroupResult.NotFound(
                    "not_found", "No existe un análisis de componentes (M2-S03) calculado para ese vector.");
            }

            var availableIds = componentSet.Components.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var missingIds = distinctIds.Where(id => !availableIds.Contains(id)).ToList();
            if (missingIds.Count > 0)
            {
                return new ComponentGroupResult.NotFound(
                    "component_not_found",
                    $"Uno o más componentes indicados no existen en la versión vigente de componentes de ese vector: {string.Join(", ", missingIds)}.");
            }

            var latest = _groupRegistry.FindLatest(projectId, imageId, vectorId);
            var existingGroups = latest?.Groups ?? Array.Empty<ComponentGroup>();

            var groupId = Guid.NewGuid();
            var name = string.IsNullOrWhiteSpace(request.Name) ? $"Grupo {existingGroups.Count + 1}" : request.Name.Trim();
            var newGroup = new ComponentGroup(groupId, name, distinctIds, componentSet.ComponentSetId, DateTimeOffset.UtcNow);

            var newGroups = existingGroups.Append(newGroup).ToList();
            var version = _groupRegistry.NextVersion(projectId, imageId, vectorId);
            var record = new ComponentGroupSetVersion(projectId, imageId, vectorId, version, newGroups, DateTimeOffset.UtcNow);
            _groupRegistry.Save(record);

            _logger.LogInformation(
                "Grupo {GroupId} ({ComponentCount} componentes) creado para el vector {VectorId} de {ProjectId}/{ImageId} (v{Version})",
                groupId, distinctIds.Count, vectorId, projectId, imageId, version);

            return new ComponentGroupResult.Ready(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ComponentGroupResult> UngroupAsync(
        Guid projectId, Guid imageId, Guid vectorId, Guid groupId, CancellationToken cancellationToken)
    {
        var gate = VectorGate(projectId, imageId, vectorId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var latest = _groupRegistry.FindLatest(projectId, imageId, vectorId);
            if (latest is null || latest.Groups.All(g => g.GroupId != groupId))
            {
                return new ComponentGroupResult.NotFound(
                    "group_not_found", "No existe ese grupo en la última versión del conjunto de grupos de ese vector.");
            }

            var newGroups = latest.Groups.Where(g => g.GroupId != groupId).ToList();
            var version = _groupRegistry.NextVersion(projectId, imageId, vectorId);
            var record = latest with { Version = version, Groups = newGroups, CreatedAt = DateTimeOffset.UtcNow };
            _groupRegistry.Save(record);

            _logger.LogInformation(
                "Grupo {GroupId} desagrupado para el vector {VectorId} de {ProjectId}/{ImageId} (v{Version}); sus componentes siguen existiendo individualmente sin ningún cambio de geometría.",
                groupId, vectorId, projectId, imageId, version);

            return new ComponentGroupResult.Ready(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ComponentGroupResult> RenameAsync(
        Guid projectId, Guid imageId, Guid vectorId, Guid groupId, ComponentGroupRenameRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ComponentGroupResult.ValidationFailed("invalid_parameters", "El nombre no puede estar vacío.");
        }

        var gate = VectorGate(projectId, imageId, vectorId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var latest = _groupRegistry.FindLatest(projectId, imageId, vectorId);
            if (latest is null || latest.Groups.All(g => g.GroupId != groupId))
            {
                return new ComponentGroupResult.NotFound(
                    "group_not_found", "No existe ese grupo en la última versión del conjunto de grupos de ese vector.");
            }

            var trimmedName = request.Name.Trim();
            var newGroups = latest.Groups.Select(g => g.GroupId == groupId ? g with { Name = trimmedName } : g).ToList();

            var version = _groupRegistry.NextVersion(projectId, imageId, vectorId);
            var record = latest with { Version = version, Groups = newGroups, CreatedAt = DateTimeOffset.UtcNow };
            _groupRegistry.Save(record);

            return new ComponentGroupResult.Ready(record);
        }
        finally
        {
            gate.Release();
        }
    }

    public ComponentGroupSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId) =>
        _groupRegistry.FindLatest(projectId, imageId, vectorId);

    private static SemaphoreSlim VectorGate(Guid projectId, Guid imageId, Guid vectorId) =>
        _vectorLocks.GetOrAdd($"{projectId:N}/{imageId:N}/{vectorId:N}", _ => new SemaphoreSlim(1, 1));
}
