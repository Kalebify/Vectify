using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Components;
using Vectify.Api.Contracts;

namespace Vectify.Api.Tests.Components;

/// <summary>
/// Pruebas unitarias de ComponentGroupService (M2-S05): validación de
/// selección/existencia de componentIds contra la ComponentSetVersion
/// vigente, ciclo agrupar/desagrupar/renombrar con versionado inmutable
/// (nunca muta una versión existente), grupos múltiples coexistiendo sin
/// exclusividad (un componente puede pertenecer a más de un grupo -- ver
/// spec.md, "Ambigüedades detectadas"), preservación EXACTA de la geometría
/// subyacente (criterio de aceptación explícito: "Agrupar/desagrupar
/// conserva exactamente los paths originales") y manejo sin crashear cuando
/// un grupo viejo queda referenciando una versión de componentes distinta.
/// </summary>
public sealed class ComponentGroupServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid VectorId = Guid.NewGuid();

    private static (ComponentGroupService Service, FakeComponentAnalysisService ComponentAnalysis, InMemoryComponentGroupVersionRegistry Registry, ComponentSetVersion ComponentSet)
        CreateService(IReadOnlyList<string>? componentIds = null)
    {
        var ids = componentIds ?? new[] { "component-1", "component-2", "component-3" };
        var componentSet = new ComponentSetVersion(
            ProjectId,
            ImageId,
            Version: 1,
            ComponentSetId: Guid.NewGuid(),
            VectorId: VectorId,
            Components: ids.Select(id => new LayerComponent(
                id,
                new List<ComponentMember> { new(0, 0, "solid", new ComponentBounds(0, 0, 10, 10), 100) },
                new ComponentBounds(0, 0, 10, 10),
                100,
                false)).ToList(),
            SkippedPathCount: 0,
            CreatedAt: DateTimeOffset.UtcNow);

        var componentAnalysis = new FakeComponentAnalysisService();
        componentAnalysis.AddComponentSet(componentSet);

        var registry = new InMemoryComponentGroupVersionRegistry();
        var service = new ComponentGroupService(componentAnalysis, registry, NullLogger<ComponentGroupService>.Instance);

        return (service, componentAnalysis, registry, componentSet);
    }

    /// <summary>Hash SHA-256 determinista del contenido de Components -- usado para comprobar que agrupar/desagrupar NUNCA lo toca.</summary>
    private static string HashComponents(IReadOnlyList<LayerComponent> components)
    {
        var json = JsonSerializer.Serialize(components);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    // ---- Validación al agrupar ----

    [Fact]
    public async Task GroupAsync_WithFewerThanTwoDistinctComponentIds_ReturnsValidationFailed()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1" }, null), CancellationToken.None);

        var failed = Assert.IsType<ComponentGroupResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
    }

    [Fact]
    public async Task GroupAsync_WithDuplicateComponentIdsOnly_ReturnsValidationFailed()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-1" }, null), CancellationToken.None);

        Assert.IsType<ComponentGroupResult.ValidationFailed>(result);
    }

    [Fact]
    public async Task GroupAsync_WhenNoComponentAnalysisExistsForVector_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();
        var otherVectorId = Guid.NewGuid();

        var result = await service.GroupAsync(
            ProjectId, ImageId, otherVectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, null), CancellationToken.None);

        var notFound = Assert.IsType<ComponentGroupResult.NotFound>(result);
        Assert.Equal("not_found", notFound.Code);
    }

    [Fact]
    public async Task GroupAsync_WhenAComponentIdDoesNotExistInTheCurrentComponentSet_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "does-not-exist" }, null), CancellationToken.None);

        var notFound = Assert.IsType<ComponentGroupResult.NotFound>(result);
        Assert.Equal("component_not_found", notFound.Code);
    }

    // ---- Agrupar exitoso ----

    [Fact]
    public async Task GroupAsync_WhenValid_CreatesFirstVersionWithOneGroupReferencingTheComponentSetId()
    {
        var (service, _, _, componentSet) = CreateService();

        var result = await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, "Base"), CancellationToken.None);

        var ready = Assert.IsType<ComponentGroupResult.Ready>(result);
        Assert.Equal(1, ready.Record.Version);
        Assert.Single(ready.Record.Groups);
        var group = ready.Record.Groups[0];
        Assert.Equal("Base", group.Name);
        Assert.Equal(new[] { "component-1", "component-2" }, group.ComponentIds);
        Assert.Equal(componentSet.ComponentSetId, group.ComponentSetId);
    }

    [Fact]
    public async Task GroupAsync_WithoutExplicitName_SynthesizesADefaultName()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, null), CancellationToken.None);

        var ready = Assert.IsType<ComponentGroupResult.Ready>(result);
        Assert.False(string.IsNullOrWhiteSpace(ready.Record.Groups[0].Name));
    }

    [Fact]
    public async Task GroupAsync_CalledTwice_CreatesTwoCoexistingGroupsEachAdvancingTheVersion()
    {
        var (service, _, _, _) = CreateService();

        var first = await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, "Grupo A"), CancellationToken.None);
        var second = await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-2", "component-3" }, "Grupo B"), CancellationToken.None);

        var firstReady = Assert.IsType<ComponentGroupResult.Ready>(first);
        var secondReady = Assert.IsType<ComponentGroupResult.Ready>(second);

        Assert.Equal(1, firstReady.Record.Version);
        Assert.Equal(2, secondReady.Record.Version);
        Assert.Equal(2, secondReady.Record.Groups.Count);

        // "component-2" pertenece a AMBOS grupos a la vez: no se prohíbe explícitamente
        // en spec.md, y restringirlo agregaría una validación no pedida (decisión
        // documentada en IMPL.md).
        Assert.Contains(secondReady.Record.Groups, g => g.Name == "Grupo A" && g.ComponentIds.Contains("component-2"));
        Assert.Contains(secondReady.Record.Groups, g => g.Name == "Grupo B" && g.ComponentIds.Contains("component-2"));
    }

    // ---- Desagrupar ----

    [Fact]
    public async Task UngroupAsync_WhenGroupDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.UngroupAsync(ProjectId, ImageId, VectorId, Guid.NewGuid(), CancellationToken.None);

        var notFound = Assert.IsType<ComponentGroupResult.NotFound>(result);
        Assert.Equal("group_not_found", notFound.Code);
    }

    [Fact]
    public async Task UngroupAsync_RemovesOnlyTheTargetGroupAndKeepsOthersAndAdvancesVersion()
    {
        var (service, _, _, _) = CreateService();
        var groupA = Assert.IsType<ComponentGroupResult.Ready>(await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, "Grupo A"), CancellationToken.None));
        var groupB = Assert.IsType<ComponentGroupResult.Ready>(await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-2", "component-3" }, "Grupo B"), CancellationToken.None));
        var groupAId = groupA.Record.Groups[0].GroupId;
        var groupBId = groupB.Record.Groups.Single(g => g.Name == "Grupo B").GroupId;

        var result = await service.UngroupAsync(ProjectId, ImageId, VectorId, groupAId, CancellationToken.None);

        var ready = Assert.IsType<ComponentGroupResult.Ready>(result);
        Assert.Equal(3, ready.Record.Version);
        Assert.Single(ready.Record.Groups);
        Assert.Equal(groupBId, ready.Record.Groups[0].GroupId);
    }

    // ---- Renombrar ----

    [Fact]
    public async Task RenameAsync_WithEmptyName_ReturnsValidationFailed()
    {
        var (service, _, _, _) = CreateService();
        var created = Assert.IsType<ComponentGroupResult.Ready>(await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, "Base"), CancellationToken.None));

        var result = await service.RenameAsync(
            ProjectId, ImageId, VectorId, created.Record.Groups[0].GroupId, new ComponentGroupRenameRequest("   "), CancellationToken.None);

        Assert.IsType<ComponentGroupResult.ValidationFailed>(result);
    }

    [Fact]
    public async Task RenameAsync_WhenGroupDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.RenameAsync(
            ProjectId, ImageId, VectorId, Guid.NewGuid(), new ComponentGroupRenameRequest("Nuevo nombre"), CancellationToken.None);

        var notFound = Assert.IsType<ComponentGroupResult.NotFound>(result);
        Assert.Equal("group_not_found", notFound.Code);
    }

    [Fact]
    public async Task RenameAsync_RenamesOnlyTheTargetGroupAndKeepsItsComponentIdsUnchanged()
    {
        var (service, _, _, _) = CreateService();
        var created = Assert.IsType<ComponentGroupResult.Ready>(await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, "Base"), CancellationToken.None));
        var groupId = created.Record.Groups[0].GroupId;

        var result = await service.RenameAsync(
            ProjectId, ImageId, VectorId, groupId, new ComponentGroupRenameRequest("  Pieza principal  "), CancellationToken.None);

        var ready = Assert.IsType<ComponentGroupResult.Ready>(result);
        Assert.Equal(2, ready.Record.Version);
        var group = ready.Record.Groups.Single(g => g.GroupId == groupId);
        Assert.Equal("Pieza principal", group.Name);
        Assert.Equal(new[] { "component-1", "component-2" }, group.ComponentIds);
    }

    // ---- Preservación EXACTA de la geometría (criterio de aceptación) ----

    [Fact]
    public async Task GroupThenUngroup_PreservesExactlyTheSameUnderlyingComponentGeometryHash()
    {
        var (service, componentAnalysis, _, componentSet) = CreateService();
        var hashBefore = HashComponents(componentAnalysis.FindLatest(ProjectId, ImageId, VectorId)!.Components);

        var grouped = Assert.IsType<ComponentGroupResult.Ready>(await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, "Base"), CancellationToken.None));
        var hashAfterGroup = HashComponents(componentAnalysis.FindLatest(ProjectId, ImageId, VectorId)!.Components);

        var ungrouped = Assert.IsType<ComponentGroupResult.Ready>(await service.UngroupAsync(
            ProjectId, ImageId, VectorId, grouped.Record.Groups[0].GroupId, CancellationToken.None));
        var hashAfterUngroup = HashComponents(componentAnalysis.FindLatest(ProjectId, ImageId, VectorId)!.Components);

        Assert.Equal(hashBefore, hashAfterGroup);
        Assert.Equal(hashBefore, hashAfterUngroup);
        Assert.Empty(ungrouped.Record.Groups);
        // Ningún componente físico desapareció ni cambió: misma cantidad, mismos IDs, mismos paths.
        Assert.Equal(componentSet.Components, componentAnalysis.FindLatest(ProjectId, ImageId, VectorId)!.Components);
    }

    // ---- Manejo sin crashear si el ComponentSetVersion referenciado cambia ----

    [Fact]
    public async Task UngroupAsync_WhenTheReferencedComponentSetWasReplaced_StillSucceedsWithoutCrashing()
    {
        var (service, componentAnalysis, _, componentSet) = CreateService();
        var created = Assert.IsType<ComponentGroupResult.Ready>(await service.GroupAsync(
            ProjectId, ImageId, VectorId, new ComponentGroupCreateRequest(new[] { "component-1", "component-2" }, "Base"), CancellationToken.None));

        // Simula que M2-S03 "recalculó" los componentes de este VectorId con un
        // ComponentSetId distinto y sin "component-1" -- el grupo viejo sigue
        // siendo válido para su propia versión, referenciada por ComponentSetId.
        var replacement = componentSet with
        {
            ComponentSetId = Guid.NewGuid(),
            Components = new List<LayerComponent> { componentSet.Components[2] },
        };
        componentAnalysis.AddComponentSet(replacement);

        var result = await service.UngroupAsync(ProjectId, ImageId, VectorId, created.Record.Groups[0].GroupId, CancellationToken.None);

        var ready = Assert.IsType<ComponentGroupResult.Ready>(result);
        Assert.Empty(ready.Record.Groups);
    }

    [Fact]
    public void ComponentGroupStaleness_WhenComponentSetIdChanged_ReportsStaleWithoutCrashing()
    {
        var group = new ComponentGroup(Guid.NewGuid(), "Base", new[] { "component-1", "component-2" }, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var currentComponents = new ComponentSetVersion(
            ProjectId, ImageId, 2, Guid.NewGuid(), VectorId,
            new List<LayerComponent> { new("component-2", new List<ComponentMember>(), new ComponentBounds(0, 0, 1, 1), 1, false) },
            0, DateTimeOffset.UtcNow);

        Assert.True(ComponentGroupStaleness.IsStale(group, currentComponents));
        Assert.Equal(new[] { "component-1" }, ComponentGroupStaleness.MissingComponentIds(group, currentComponents));
    }

    [Fact]
    public void ComponentGroupStaleness_WhenNoComponentAnalysisExists_ReportsStaleWithAllIdsMissingWithoutCrashing()
    {
        var group = new ComponentGroup(Guid.NewGuid(), "Base", new[] { "component-1", "component-2" }, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.True(ComponentGroupStaleness.IsStale(group, currentComponents: null));
        Assert.Equal(group.ComponentIds, ComponentGroupStaleness.MissingComponentIds(group, currentComponents: null));
    }

    // ---- FindLatest ----

    [Fact]
    public void FindLatest_WhenNeverGrouped_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        Assert.Null(service.FindLatest(ProjectId, ImageId, VectorId));
    }
}
