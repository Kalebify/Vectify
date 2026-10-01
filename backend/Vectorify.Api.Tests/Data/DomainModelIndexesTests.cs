using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Vectorify.Api.Data;

namespace Vectorify.Api.Tests.Data;

/// <summary>
/// Test LIVIANO (sin Postgres real, sin Docker) que inspecciona el modelo de EF Core
/// construido por <see cref="VectorizationDbContext.OnModelCreating"/> -- confirma que
/// los índices y el query filter de soft delete pedidos explícitamente por M2.2-S02
/// existen en el modelo. <c>UseNpgsql</c> con una connection string ficticia solo
/// configura el provider: construir <see cref="DbContext.Model"/> nunca abre una
/// conexión real (mismo principio que <c>VectorizationDbContextFactory</c>, que usa el
/// modelo sin conectar para <c>dotnet ef migrations add</c>).
/// </summary>
public sealed class DomainModelIndexesTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<VectorizationDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only;Username=x;Password=x")
            .Options;
        using var dbContext = new VectorizationDbContext(options);
        return dbContext.Model;
    }

    [Fact]
    public void Project_HasIndexes_OnOwnerIdAndUpdatedAt()
    {
        var entity = BuildModel().FindEntityType(typeof(Project))!;

        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "OwnerId" }));
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "UpdatedAt" }));
    }

    [Fact]
    public void Asset_HasIndex_OnProjectId()
    {
        var entity = BuildModel().FindEntityType(typeof(Asset))!;

        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ProjectId" }));
    }

    [Fact]
    public void VectorDocument_HasIndex_OnProjectId()
    {
        var entity = BuildModel().FindEntityType(typeof(VectorDocument))!;

        Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ProjectId" }));
    }

    [Fact]
    public void DocumentVersion_HasUniqueCompositeIndex_OnVectorDocumentIdAndVersionNumber()
    {
        var entity = BuildModel().FindEntityType(typeof(DocumentVersion))!;

        var index = Assert.Single(
            entity.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "VectorDocumentId", "VersionNumber" }));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void DocumentVersion_UniqueIndex_IsNotGlobalAcrossVersionNumberAlone()
    {
        var entity = BuildModel().FindEntityType(typeof(DocumentVersion))!;

        // No debe existir un índice único SOLO sobre VersionNumber -- sería un
        // constraint global incorrecto; la tarjeta pide único POR VectorDocumentId.
        Assert.DoesNotContain(
            entity.GetIndexes(),
            i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "VersionNumber" }));
    }

    [Fact]
    public void Project_HasGlobalQueryFilter_ForSoftDelete()
    {
        var entity = BuildModel().FindEntityType(typeof(Project))!;

        Assert.NotNull(entity.GetQueryFilter());
    }
}
