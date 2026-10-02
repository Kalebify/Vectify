using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Vectorify.Api.Data;
using Vectorify.Api.ManufacturingOperations;
using Vectorify.Api.Projects.Persistence;
using Vectorify.Api.VectorDocuments;
using Vectorify.Api.VectorDocuments.Persistence;

namespace Vectorify.Api.Tests.VectorDocuments.Persistence;

/// <summary>
/// Tests de integración de <see cref="VectorDocumentRepository"/> (M2.2-S05) directamente
/// contra PostgreSQL real (Testcontainers, NUNCA UseInMemoryDatabase -- mismo criterio que el
/// resto de MVP2.2): round-trip del grafo completo, el upsert-por-Id de Layer (ver el reporte
/// del sprint para el razonamiento de por qué hace falta), ownership y concurrencia. Ejercita
/// el repositorio sin pasar por VectorDocumentService ni por HTTP -- eso lo cubre
/// Vectorify.Api.Tests.EndToEnd.VectorDocumentEndpointsTests.
/// </summary>
public sealed class VectorDocumentRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SaveAsync_NewProject_CreatesDocumentVersionLayersAndPaletteColors()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var project = await new ProjectRepository(dbContext).CreateAsync(ownerId, "Proyecto de prueba", null, CancellationToken.None);
        var repository = new VectorDocumentRepository(dbContext);

        var layerId = Guid.NewGuid();
        var assetId = await SeedAssetAsync(dbContext, project.Id, "layer-svg");
        var snapshot = new DocumentSnapshot(
            WidthMm: 100,
            HeightMm: 50,
            ViewBox: "0 0 1000 500",
            SchemaVersion: 1,
            Origin: "workspace_save",
            MetadataJson: "{}",
            Layers: [new LayerSnapshot(layerId, "Capa 1", 0, true, false, ManufacturingOperationKind.Cut, assetId, new PaletteColorSnapshot("#112233", 80.0, false, 0), PathCount: 7)]);

        var outcome = await repository.SaveAsync(project.Id, ownerId, snapshot, CancellationToken.None);

        Assert.NotNull(outcome);
        Assert.Equal(project.Id, outcome!.ProjectId);
        Assert.Equal(1, outcome.VersionNumber);

        await using var readContext = CreateDbContext();
        var reloadedProject = await readContext.Projects
            .Include(p => p.VectorDocuments).ThenInclude(d => d.Versions).ThenInclude(v => v.Layers)
            .Include(p => p.VectorDocuments).ThenInclude(d => d.Versions).ThenInclude(v => v.PaletteColors)
            .FirstAsync(p => p.Id == project.Id);

        var document = Assert.Single(reloadedProject.VectorDocuments);
        Assert.Equal(100, document.WidthMm);
        Assert.Equal("0 0 1000 500", document.ViewBox);
        Assert.Equal(1, document.SchemaVersion);

        var version = Assert.Single(document.Versions);
        Assert.Equal(reloadedProject.CurrentVersionId, version.Id);

        var layer = Assert.Single(version.Layers);
        Assert.Equal(layerId, layer.Id); // groupId reutilizado verbatim
        Assert.Equal("Capa 1", layer.Name);
        Assert.Equal(ManufacturingOperationKind.Cut, layer.ManufacturingOperation);
        Assert.Equal(assetId, layer.SvgAssetId);
        Assert.Equal(7, layer.PathCount); // bug real encontrado en revisión: ver Data.Layer.PathCount

        var color = Assert.Single(version.PaletteColors);
        Assert.Equal(layer.ColorId, color.Id);
        Assert.Equal("#112233", color.Hex);
    }

    [Fact]
    public async Task SaveAsync_SecondSaveReusingSameLayerId_UpsertsInPlaceInsteadOfViolatingThePrimaryKey()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var project = await new ProjectRepository(dbContext).CreateAsync(ownerId, "Proyecto reutilizado", null, CancellationToken.None);
        var repository = new VectorDocumentRepository(dbContext);

        var stableLayerId = Guid.NewGuid();
        var firstAssetId = await SeedAssetAsync(dbContext, project.Id, "layer-svg");

        var firstSnapshot = new DocumentSnapshot(
            10, 10, "0 0 100 100", 1, "workspace_save", "{}",
            [new LayerSnapshot(stableLayerId, "Capa original", 0, true, false, null, firstAssetId, new PaletteColorSnapshot("#ff0000", 100, false, 0), PathCount: 3)]);
        var firstOutcome = await repository.SaveAsync(project.Id, ownerId, firstSnapshot, CancellationToken.None);
        Assert.Equal(1, firstOutcome!.VersionNumber);

        // Mismo groupId (stableLayerId) reutilizado VERBATIM en la segunda versión -- sin esto,
        // un segundo Save real del mismo Workspace violaría la PK global de "layers" (ver
        // VectorDocumentRepository.SaveAsync).
        var secondAssetId = await SeedAssetAsync(dbContext, project.Id, "layer-svg");
        var secondSnapshot = new DocumentSnapshot(
            10, 10, "0 0 100 100", 1, "workspace_save", "{}",
            [new LayerSnapshot(stableLayerId, "Capa renombrada", 1, false, true, ManufacturingOperationKind.Engrave, secondAssetId, new PaletteColorSnapshot("#00ff00", 100, false, 0), PathCount: 9)]);
        var secondOutcome = await repository.SaveAsync(project.Id, ownerId, secondSnapshot, CancellationToken.None);

        Assert.Equal(2, secondOutcome!.VersionNumber);

        await using var readContext = CreateDbContext();
        var layersWithThatId = await readContext.Layers.Where(l => l.Id == stableLayerId).ToListAsync();
        var onlyLayer = Assert.Single(layersWithThatId); // una sola fila en TODA la tabla, nunca dos
        Assert.Equal("Capa renombrada", onlyLayer.Name);
        Assert.False(onlyLayer.Visible);
        Assert.True(onlyLayer.Locked);
        Assert.Equal(ManufacturingOperationKind.Engrave, onlyLayer.ManufacturingOperation);
        Assert.Equal(secondAssetId, onlyLayer.SvgAssetId);
        Assert.Equal(9, onlyLayer.PathCount); // repunta junto con el resto de los campos, no solo Id

        var reloadedProject = await readContext.Projects.FirstAsync(p => p.Id == project.Id);
        var currentVersion = await readContext.DocumentVersions
            .Include(v => v.Layers)
            .FirstAsync(v => v.Id == reloadedProject.CurrentVersionId);
        Assert.Equal(2, currentVersion.VersionNumber);
        Assert.Contains(currentVersion.Layers, l => l.Id == stableLayerId);
    }

    [Fact]
    public async Task SaveAsync_NonexistentOrWrongOwner_ReturnsNull()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new VectorDocumentRepository(dbContext);

        var snapshot = new DocumentSnapshot(1, 1, "0 0 1 1", 1, "workspace_save", "{}", []);
        var outcome = await repository.SaveAsync(Guid.NewGuid(), ownerId, snapshot, CancellationToken.None);

        Assert.Null(outcome);
    }

    [Fact]
    public async Task FindCurrentDocumentAsync_ProjectWithoutAnySavedVersion_ReturnsNull()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var project = await new ProjectRepository(dbContext).CreateAsync(ownerId, "Proyecto sin guardar", null, CancellationToken.None);
        var repository = new VectorDocumentRepository(dbContext);

        var found = await repository.FindCurrentDocumentAsync(project.Id, ownerId, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task UpdateLayerAsync_WithPartialFields_PatchesOnlyThoseFields()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var project = await new ProjectRepository(dbContext).CreateAsync(ownerId, "Proyecto a patchear", null, CancellationToken.None);
        var repository = new VectorDocumentRepository(dbContext);

        var layerId = Guid.NewGuid();
        var assetId = await SeedAssetAsync(dbContext, project.Id, "layer-svg");
        await repository.SaveAsync(
            project.Id, ownerId,
            new DocumentSnapshot(1, 1, "0 0 1 1", 1, "workspace_save", "{}",
                [new LayerSnapshot(layerId, "Original", 0, true, false, null, assetId, new PaletteColorSnapshot("#000000", 100, false, 0), PathCount: 1)]),
            CancellationToken.None);

        var patched = await repository.UpdateLayerAsync(
            project.Id, ownerId, layerId, new LayerPatch(Name: null, Order: 5, Visible: false, Locked: null, TouchOperation: true, Operation: ManufacturingOperationKind.Ignore),
            CancellationToken.None);

        Assert.NotNull(patched);
        Assert.Equal("Original", patched!.Name); // null = sin cambios
        Assert.Equal(5, patched.Order);
        Assert.False(patched.Visible);
        Assert.False(patched.Locked); // no tocado, preserva el valor anterior
        Assert.Equal(ManufacturingOperationKind.Ignore, patched.ManufacturingOperation);
    }

    [Fact]
    public async Task UpdateLayerAsync_LayerBelongsToASupersededHistoricalVersion_ReturnsNull()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var project = await new ProjectRepository(dbContext).CreateAsync(ownerId, "Proyecto con capa removida", null, CancellationToken.None);
        var repository = new VectorDocumentRepository(dbContext);

        var keptLayerId = Guid.NewGuid();
        var removedLayerId = Guid.NewGuid();
        var assetId1 = await SeedAssetAsync(dbContext, project.Id, "layer-svg");
        var assetId2 = await SeedAssetAsync(dbContext, project.Id, "layer-svg");

        await repository.SaveAsync(
            project.Id, ownerId,
            new DocumentSnapshot(1, 1, "0 0 1 1", 1, "workspace_save", "{}",
            [
                new LayerSnapshot(keptLayerId, "Se mantiene", 0, true, false, null, assetId1, new PaletteColorSnapshot("#111111", 50, false, 0), PathCount: 1),
                new LayerSnapshot(removedLayerId, "Se va a quitar", 1, true, false, null, assetId2, new PaletteColorSnapshot("#222222", 50, false, 1), PathCount: 1),
            ]),
            CancellationToken.None);

        var assetId3 = await SeedAssetAsync(dbContext, project.Id, "layer-svg");
        // Segundo Save: "removedLayerId" ya no está presente -- su fila queda atrás, colgada de
        // la DocumentVersion ANTERIOR (ya no la actual del proyecto).
        await repository.SaveAsync(
            project.Id, ownerId,
            new DocumentSnapshot(1, 1, "0 0 1 1", 1, "workspace_save", "{}",
            [
                new LayerSnapshot(keptLayerId, "Se mantiene", 0, true, false, null, assetId3, new PaletteColorSnapshot("#111111", 100, false, 0), PathCount: 1),
            ]),
            CancellationToken.None);

        var patched = await repository.UpdateLayerAsync(
            project.Id, ownerId, removedLayerId, new LayerPatch("Intento de editar", null, null, null, false, null), CancellationToken.None);

        Assert.Null(patched);
    }

    [Fact]
    public async Task UpdateLayerAsync_WrongProjectId_ReturnsNull()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var project = await new ProjectRepository(dbContext).CreateAsync(ownerId, "Proyecto A", null, CancellationToken.None);
        var otherProject = await new ProjectRepository(dbContext).CreateAsync(ownerId, "Proyecto B", null, CancellationToken.None);
        var repository = new VectorDocumentRepository(dbContext);

        var layerId = Guid.NewGuid();
        var assetId = await SeedAssetAsync(dbContext, project.Id, "layer-svg");
        await repository.SaveAsync(
            project.Id, ownerId,
            new DocumentSnapshot(1, 1, "0 0 1 1", 1, "workspace_save", "{}",
                [new LayerSnapshot(layerId, "Capa", 0, true, false, null, assetId, new PaletteColorSnapshot("#000000", 100, false, 0), PathCount: 1)]),
            CancellationToken.None);

        var patched = await repository.UpdateLayerAsync(
            otherProject.Id, ownerId, layerId, new LayerPatch("Otro nombre", null, null, null, false, null), CancellationToken.None);

        Assert.Null(patched);
    }

    private static async Task<Guid> SeedUserAsync(VectorizationDbContext dbContext)
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Usuario de prueba", CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> SeedAssetAsync(VectorizationDbContext dbContext, Guid projectId, string type)
    {
        var assetId = Guid.NewGuid();
        dbContext.Assets.Add(new Asset
        {
            Id = assetId,
            ProjectId = projectId,
            Type = type,
            StorageKey = $"projects/{projectId:N}/{type}/{assetId:N}.svg",
            MimeType = "image/svg+xml",
            FileName = $"{assetId:N}.svg",
            Size = 10,
            Checksum = "deadbeef",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();
        return assetId;
    }

    private async Task<VectorizationDbContext> CreateMigratedDbContextAsync()
    {
        var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }

    private VectorizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<VectorizationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new VectorizationDbContext(options);
    }
}
