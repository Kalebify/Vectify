using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Vectorify.Api.Data;
using Vectorify.Api.Projects.Persistence;
using Vectorify.Api.VectorDocuments;

namespace Vectorify.Api.Tests.Projects.Persistence;

/// <summary>
/// Tests de integración de <see cref="ProjectRepository"/> (M2.2-S03) directamente contra
/// PostgreSQL real (Testcontainers, NUNCA UseInMemoryDatabase -- mismo criterio que
/// S01/S02): CRUD, ownership, paginación/búsqueda/orden, soft-delete, duplicate y
/// concurrencia optimista (xmin). Ejercita el repositorio sin pasar por ProjectService ni
/// por HTTP -- eso lo cubre Vectorify.Api.Tests.EndToEnd.ProjectV2EndpointsTests.
/// </summary>
public sealed class ProjectRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task CreateAsync_PersistsProject_OwnedByGivenOwnerId()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        var created = await repository.CreateAsync(ownerId, "Placa grabada", "Descripción de prueba", CancellationToken.None);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(ownerId, created.OwnerId);
        Assert.Equal("Placa grabada", created.Name);
        Assert.Equal("Descripción de prueba", created.Description);
        Assert.Null(created.DeletedAt);

        await using var readContext = CreateDbContext();
        var reloaded = await readContext.Projects.FindAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Placa grabada", reloaded!.Name);
    }

    [Fact]
    public async Task FindByIdAsync_NonexistentId_ReturnsNull()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        var found = await repository.FindByIdAsync(Guid.NewGuid(), ownerId, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindByIdAsync_ProjectBelongsToAnotherOwner_ReturnsNull()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var strangerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        var project = await repository.CreateAsync(ownerId, "Proyecto de otro usuario", null, CancellationToken.None);

        // El mismo Id, pero consultado como si fuera el usuario "stranger": debe comportarse
        // IGUAL que un Id inexistente (null), nunca revelar que el proyecto existe.
        var found = await repository.FindByIdAsync(project.Id, strangerId, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task UpdateAsync_WithNonNullFields_RenamesAndUpdatesDescription()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);
        var project = await repository.CreateAsync(ownerId, "Nombre original", "Descripción original", CancellationToken.None);

        var updated = await repository.UpdateAsync(project.Id, ownerId, "Nombre nuevo", "Descripción nueva", CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Nombre nuevo", updated!.Name);
        Assert.Equal("Descripción nueva", updated.Description);
        Assert.True(updated.UpdatedAt > updated.CreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_WithNullFields_LeavesExistingValuesUnchanged()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);
        var project = await repository.CreateAsync(ownerId, "Nombre original", "Descripción original", CancellationToken.None);

        var updated = await repository.UpdateAsync(project.Id, ownerId, null, null, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Nombre original", updated!.Name);
        Assert.Equal("Descripción original", updated.Description);
    }

    [Fact]
    public async Task UpdateAsync_NonexistentOrWrongOwner_ReturnsNull()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        var updated = await repository.UpdateAsync(Guid.NewGuid(), ownerId, "No importa", null, CancellationToken.None);

        Assert.Null(updated);
    }

    [Fact]
    public async Task UpdateAsync_TwoConcurrentUpdates_SecondSaveThrowsDbUpdateConcurrencyException()
    {
        await using var seedContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(seedContext);
        var project = await new ProjectRepository(seedContext).CreateAsync(ownerId, "Proyecto concurrente", null, CancellationToken.None);

        // Dos DbContext INDEPENDIENTES cargan la MISMA fila (mismo valor de xmin en ambos)
        // -- el "warm up" read ANTES de llamar UpdateAsync es necesario porque, dentro del
        // MISMO DbContext, una query posterior sobre una entidad YA trackeada devuelve la
        // MISMA instancia sin refrescar sus valores (identity resolution de EF Core):
        // así es como cada contexto se queda "anclado" al xmin viejo incluso después de
        // que el otro haya escrito.
        await using var contextA = CreateDbContext();
        await using var contextB = CreateDbContext();
        await contextA.Projects.FirstAsync(p => p.Id == project.Id);
        await contextB.Projects.FirstAsync(p => p.Id == project.Id);
        var repositoryA = new ProjectRepository(contextA);
        var repositoryB = new ProjectRepository(contextB);

        var projectA = await repositoryA.UpdateAsync(project.Id, ownerId, "Actualizado por A", null, CancellationToken.None);
        Assert.NotNull(projectA);

        // repositoryB todavía tiene el xmin VIEJO (lo cargó antes de que A guardara) ->
        // el UPDATE de B no afecta ninguna fila (el WHERE incluye xmin = valor viejo) ->
        // EF Core lo traduce a DbUpdateConcurrencyException real (concurrencia optimista
        // de PostgreSQL, no un mock).
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => repositoryB.UpdateAsync(project.Id, ownerId, "Actualizado por B", null, CancellationToken.None));

        await using var readContext = CreateDbContext();
        var reloaded = await readContext.Projects.FindAsync(project.Id);
        Assert.Equal("Actualizado por A", reloaded!.Name);
    }

    [Fact]
    public async Task SoftDeleteAsync_ExcludesProjectFromNormalQueries_ButRowStillExistsInDatabase()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);
        var project = await repository.CreateAsync(ownerId, "Proyecto a borrar", null, CancellationToken.None);

        var deleted = await repository.SoftDeleteAsync(project.Id, ownerId, CancellationToken.None);
        Assert.True(deleted);

        // No aparece en FindByIdAsync (el global query filter de M2.2-S02 lo excluye).
        var found = await repository.FindByIdAsync(project.Id, ownerId, CancellationToken.None);
        Assert.Null(found);

        // No aparece en el listado.
        var (items, totalCount) = await repository.ListAsync(
            ownerId, new ProjectListQuery(1, 20, null, ProjectSortBy.LastModified), CancellationToken.None);
        Assert.Empty(items);
        Assert.Equal(0, totalCount);

        // Pero la fila SIGUE existiendo en la base -- verificable con IgnoreQueryFilters().
        await using var readContext = CreateDbContext();
        var withDeleted = await readContext.Projects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == project.Id);
        Assert.NotNull(withDeleted);
        Assert.NotNull(withDeleted!.DeletedAt);
    }

    [Fact]
    public async Task SoftDeleteAsync_NonexistentOrWrongOwner_ReturnsFalse()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        var deleted = await repository.SoftDeleteAsync(Guid.NewGuid(), ownerId, CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task ListAsync_WithMoreResultsThanOnePage_PaginatesCorrectly()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        for (var i = 0; i < 25; i++)
        {
            await repository.CreateAsync(ownerId, $"Proyecto {i:D2}", null, CancellationToken.None);
        }

        var (firstPageItems, totalCount) = await repository.ListAsync(
            ownerId, new ProjectListQuery(1, 10, null, ProjectSortBy.Name), CancellationToken.None);
        var (secondPageItems, _) = await repository.ListAsync(
            ownerId, new ProjectListQuery(2, 10, null, ProjectSortBy.Name), CancellationToken.None);
        var (thirdPageItems, _) = await repository.ListAsync(
            ownerId, new ProjectListQuery(3, 10, null, ProjectSortBy.Name), CancellationToken.None);

        Assert.Equal(25, totalCount);
        Assert.Equal(10, firstPageItems.Count);
        Assert.Equal(10, secondPageItems.Count);
        Assert.Equal(5, thirdPageItems.Count);

        // Ningún Id se repite entre páginas (corta correctamente, no duplica/salta filas).
        var allIds = firstPageItems.Concat(secondPageItems).Concat(thirdPageItems).Select(p => p.Id).ToList();
        Assert.Equal(25, allIds.Distinct().Count());
    }

    [Fact]
    public async Task ListAsync_WithSearch_FiltersByPartialCaseInsensitiveName()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        await repository.CreateAsync(ownerId, "Llavero laser", null, CancellationToken.None);
        await repository.CreateAsync(ownerId, "Placa grabada", null, CancellationToken.None);
        await repository.CreateAsync(ownerId, "Otro LLAVERO con mayúsculas", null, CancellationToken.None);

        var (items, totalCount) = await repository.ListAsync(
            ownerId, new ProjectListQuery(1, 20, "llavero", ProjectSortBy.Name), CancellationToken.None);

        Assert.Equal(2, totalCount);
        Assert.All(items, p => Assert.Contains("llavero", p.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListAsync_SortByName_ReturnsAlphabeticalAscending()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        await repository.CreateAsync(ownerId, "Charlie", null, CancellationToken.None);
        await repository.CreateAsync(ownerId, "Alpha", null, CancellationToken.None);
        await repository.CreateAsync(ownerId, "Bravo", null, CancellationToken.None);

        var (items, _) = await repository.ListAsync(
            ownerId, new ProjectListQuery(1, 20, null, ProjectSortBy.Name), CancellationToken.None);

        Assert.Equal(["Alpha", "Bravo", "Charlie"], items.Select(p => p.Name));
    }

    [Fact]
    public async Task ListAsync_SortByCreated_ReturnsMostRecentlyCreatedFirst()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        var first = await repository.CreateAsync(ownerId, "Primero", null, CancellationToken.None);
        await Task.Delay(10);
        var second = await repository.CreateAsync(ownerId, "Segundo", null, CancellationToken.None);

        var (items, _) = await repository.ListAsync(
            ownerId, new ProjectListQuery(1, 20, null, ProjectSortBy.Created), CancellationToken.None);

        Assert.Equal([second.Id, first.Id], items.Select(p => p.Id));
    }

    [Fact]
    public async Task ListAsync_SortByLastModified_ReturnsMostRecentlyUpdatedFirst()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        var first = await repository.CreateAsync(ownerId, "Primero", null, CancellationToken.None);
        var second = await repository.CreateAsync(ownerId, "Segundo", null, CancellationToken.None);

        // Toca "first" DESPUÉS de crear ambos: debe pasar a estar primero por LastModified,
        // aunque fue creado antes que "second".
        await Task.Delay(10);
        await repository.UpdateAsync(first.Id, ownerId, null, "Tocado de nuevo", CancellationToken.None);

        var (items, _) = await repository.ListAsync(
            ownerId, new ProjectListQuery(1, 20, null, ProjectSortBy.LastModified), CancellationToken.None);

        Assert.Equal([first.Id, second.Id], items.Select(p => p.Id));
    }

    [Fact]
    public async Task DuplicateAsync_CreatesNewProjectWithNewId_CopyingDataWithoutReusingOriginalIds()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);
        var source = await repository.CreateAsync(ownerId, "Proyecto original", "Descripción original", CancellationToken.None);

        var duplicate = await repository.DuplicateAsync(source.Id, ownerId, "Copia de Proyecto original", CancellationToken.None);

        Assert.NotNull(duplicate);
        Assert.NotEqual(source.Id, duplicate!.Id);
        Assert.Equal("Copia de Proyecto original", duplicate.Name);
        Assert.Equal(source.Description, duplicate.Description);
        Assert.Equal(ownerId, duplicate.OwnerId);

        // El original sigue existiendo, sin cambios.
        var reloadedSource = await repository.FindByIdAsync(source.Id, ownerId, CancellationToken.None);
        Assert.NotNull(reloadedSource);
        Assert.Equal("Proyecto original", reloadedSource!.Name);
    }

    [Fact]
    public async Task DuplicateAsync_WithVectorDocumentGraph_DuplicatesEverythingWithFreshIds()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);
        var source = await repository.CreateAsync(ownerId, "Proyecto con documento", null, CancellationToken.None);

        var document = new VectorDocument
        {
            Id = Guid.NewGuid(),
            ProjectId = source.Id,
        };
        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VectorDocumentId = document.Id,
            VersionNumber = 1,
            WidthMm = 100,
            HeightMm = 50,
            ViewBox = "0 0 1000 500",
            SchemaVersion = 1,
            Origin = DocumentVersionOrigin.Vectorize,
            MetadataJson = "{\"engine\":\"test\"}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var color = new PaletteColor
        {
            Id = Guid.NewGuid(),
            VersionId = version.Id,
            Hex = "#112233",
            Coverage = 42.5,
            IsBackground = false,
            Order = 0,
        };
        var layer = new Layer
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.NewGuid(),
            VersionId = version.Id,
            ColorId = color.Id,
            Name = "Capa 1",
            Order = 0,
            Visible = true,
            Locked = false,
        };

        dbContext.VectorDocuments.Add(document);
        dbContext.DocumentVersions.Add(version);
        dbContext.PaletteColors.Add(color);
        dbContext.Layers.Add(layer);
        await dbContext.SaveChangesAsync();

        // CurrentVersionId del proyecto origen apunta a la versión recién creada -- el
        // duplicado debe apuntar a la versión NUEVA correspondiente, no a esta.
        var trackedSource = await dbContext.Projects.FirstAsync(p => p.Id == source.Id);
        trackedSource.CurrentVersionId = version.Id;
        await dbContext.SaveChangesAsync();

        var duplicate = await repository.DuplicateAsync(source.Id, ownerId, "Copia", CancellationToken.None);
        Assert.NotNull(duplicate);

        await using var readContext = CreateDbContext();
        var reloadedDuplicate = await readContext.Projects
            .Include(p => p.VectorDocuments).ThenInclude(d => d.Versions).ThenInclude(v => v.PaletteColors)
            .Include(p => p.VectorDocuments).ThenInclude(d => d.Versions).ThenInclude(v => v.Layers)
            .FirstAsync(p => p.Id == duplicate!.Id);

        var duplicatedDocument = Assert.Single(reloadedDuplicate.VectorDocuments);
        Assert.NotEqual(document.Id, duplicatedDocument.Id);

        var duplicatedVersion = Assert.Single(duplicatedDocument.Versions);
        Assert.NotEqual(version.Id, duplicatedVersion.Id);
        Assert.Equal(version.VersionNumber, duplicatedVersion.VersionNumber);
        // M2.2-S06: WidthMm/HeightMm/ViewBox/SchemaVersion viven en DocumentVersion -- se
        // copian de la versión origen, no del VectorDocument (que ya no los tiene).
        Assert.Equal(version.WidthMm, duplicatedVersion.WidthMm);
        Assert.Equal(version.ViewBox, duplicatedVersion.ViewBox);
        // Comparado contra el valor releído de jsonb (no el string en memoria original):
        // PostgreSQL normaliza el whitespace de jsonb al guardar (ej. agrega un espacio
        // después de ":"), así que el string crudo post-round-trip no es byte-a-byte
        // idéntico al que se insertó -- la igualdad que importa es la del CONTENIDO, que sí
        // se preserva (ver la comparación contra reloadedSourceVersion más abajo).
        var reloadedSourceVersion = await readContext.DocumentVersions.FirstAsync(v => v.Id == version.Id);
        Assert.Equal(reloadedSourceVersion.MetadataJson, duplicatedVersion.MetadataJson);

        // CurrentVersionId del duplicado apunta a la versión NUEVA, no a la original.
        Assert.Equal(duplicatedVersion.Id, reloadedDuplicate.CurrentVersionId);

        var duplicatedColor = Assert.Single(duplicatedVersion.PaletteColors);
        Assert.NotEqual(color.Id, duplicatedColor.Id);
        Assert.Equal(color.Hex, duplicatedColor.Hex);

        var duplicatedLayer = Assert.Single(duplicatedVersion.Layers);
        Assert.NotEqual(layer.Id, duplicatedLayer.Id);
        Assert.Equal(layer.GroupId, duplicatedLayer.GroupId); // groupId clásico preservado TAL CUAL
        Assert.Equal(duplicatedColor.Id, duplicatedLayer.ColorId); // apunta al color DUPLICADO, no al original
        Assert.Equal(layer.Name, duplicatedLayer.Name);

        // El original sigue intacto, con sus Ids originales, sin tocar.
        var reloadedSource = await readContext.Projects
            .Include(p => p.VectorDocuments).ThenInclude(d => d.Versions)
            .FirstAsync(p => p.Id == source.Id);
        Assert.Equal(document.Id, Assert.Single(reloadedSource.VectorDocuments).Id);
    }

    [Fact]
    public async Task DuplicateAsync_NonexistentOrWrongOwner_ReturnsNull()
    {
        await using var dbContext = await CreateMigratedDbContextAsync();
        var ownerId = await SeedUserAsync(dbContext);
        var repository = new ProjectRepository(dbContext);

        var duplicate = await repository.DuplicateAsync(Guid.NewGuid(), ownerId, "Copia", CancellationToken.None);

        Assert.Null(duplicate);
    }

    private static async Task<Guid> SeedUserAsync(VectorizationDbContext dbContext)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Usuario de prueba",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
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
