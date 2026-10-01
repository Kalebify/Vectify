using Microsoft.EntityFrameworkCore;
using Vectorify.Api.Data;

namespace Vectorify.Api.Projects.Persistence;

/// <summary>
/// Implementación EF Core de <see cref="IProjectRepository"/> (M2.2-S03) contra
/// <see cref="VectorizationDbContext"/>. Único punto del código que emite queries EF
/// directas sobre <c>DbSet&lt;Project&gt;</c> para este caso de uso -- ver
/// IProjectRepository para por qué vive en este namespace.
/// </summary>
public sealed class ProjectRepository : IProjectRepository
{
    private readonly VectorizationDbContext _dbContext;

    public ProjectRepository(VectorizationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Project> CreateAsync(Guid ownerId, string name, string? description, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = name,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return project;
    }

    public Task<Project?> FindByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, cancellationToken);

    public async Task<(IReadOnlyList<Project> Items, int TotalCount)> ListAsync(
        Guid ownerId, ProjectListQuery query, CancellationToken cancellationToken)
    {
        var baseQuery = _dbContext.Projects.Where(p => p.OwnerId == ownerId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ILIKE: búsqueda case-insensitive nativa de PostgreSQL (EF.Functions.ILike,
            // provisto por Npgsql.EntityFrameworkCore.PostgreSQL) -- se traduce a SQL, no
            // trae todas las filas a memoria para filtrar client-side.
            var pattern = $"%{query.Search.Trim()}%";
            baseQuery = baseQuery.Where(p => EF.Functions.ILike(p.Name, pattern));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        baseQuery = query.SortBy switch
        {
            ProjectSortBy.Name => baseQuery.OrderBy(p => p.Name),
            ProjectSortBy.Created => baseQuery.OrderByDescending(p => p.CreatedAt),
            _ => baseQuery.OrderByDescending(p => p.UpdatedAt),
        };

        var items = await baseQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Project?> UpdateAsync(Guid id, Guid ownerId, string? name, string? description, CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        if (name is not null)
        {
            project.Name = name;
        }

        if (description is not null)
        {
            project.Description = description;
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;

        // Si otra request actualizó esta misma fila entre el FirstOrDefaultAsync de arriba
        // y este SaveChangesAsync, el valor de "xmin" que EF Core envía en el WHERE del
        // UPDATE ya no coincide con el de la fila real -> 0 filas afectadas -> EF Core
        // lanza DbUpdateConcurrencyException (concurrencia optimista real, no simulada).
        await _dbContext.SaveChangesAsync(cancellationToken);

        return project;
    }

    public async Task<bool> SoftDeleteAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, cancellationToken);
        if (project is null)
        {
            return false;
        }

        // Política de Assets (ver IMPL.md): los Assets de este proyecto NO se tocan acá --
        // ni se borran ni se desvinculan. Siguen existiendo en la tabla, simplemente
        // inalcanzables a través de este Project mientras el global query filter
        // (DeletedAt == null) lo excluya.
        var now = DateTimeOffset.UtcNow;
        project.DeletedAt = now;
        project.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<Project?> DuplicateAsync(Guid id, Guid ownerId, string newName, CancellationToken cancellationToken)
    {
        var source = await _dbContext.Projects
            .Include(p => p.VectorDocuments).ThenInclude(d => d.Versions).ThenInclude(v => v.PaletteColors)
            .Include(p => p.VectorDocuments).ThenInclude(d => d.Versions).ThenInclude(v => v.Layers)
            .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, cancellationToken);

        if (source is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var duplicate = new Project
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = newName,
            Description = source.Description,
            CreatedAt = now,
            UpdatedAt = now,

            // ThumbnailAssetId deliberadamente NO se copia: los Assets no se duplican acá
            // (ver clase), y apuntar al Asset del proyecto ORIGINAL desde el duplicado
            // mezclaría la identidad de ambos proyectos -- más simple y correcto dejarlo
            // sin thumbnail hasta que algo lo genere de nuevo para el duplicado.
            ThumbnailAssetId = null,
        };

        Guid? duplicateCurrentVersionId = null;

        foreach (var document in source.VectorDocuments)
        {
            var newDocument = new VectorDocument
            {
                Id = Guid.NewGuid(),
                ProjectId = duplicate.Id,
                WidthMm = document.WidthMm,
                HeightMm = document.HeightMm,
                ViewBox = document.ViewBox,
                SchemaVersion = document.SchemaVersion,
            };

            foreach (var version in document.Versions)
            {
                var newVersion = new DocumentVersion
                {
                    Id = Guid.NewGuid(),
                    VectorDocumentId = newDocument.Id,
                    VersionNumber = version.VersionNumber,
                    // El Asset SVG en sí NO se duplica (binario inmutable, fuera de
                    // alcance -- ver IMPL.md): la versión duplicada referencia el MISMO
                    // Asset que la original.
                    SvgAssetId = version.SvgAssetId,
                    Origin = version.Origin,
                    MetadataJson = version.MetadataJson,
                    CreatedAt = now,
                };

                if (source.CurrentVersionId == version.Id)
                {
                    duplicateCurrentVersionId = newVersion.Id;
                }

                var colorIdMap = new Dictionary<Guid, Guid>();
                foreach (var color in version.PaletteColors)
                {
                    var newColor = new PaletteColor
                    {
                        Id = Guid.NewGuid(),
                        VersionId = newVersion.Id,
                        Hex = color.Hex,
                        Coverage = color.Coverage,
                        IsBackground = color.IsBackground,
                        Order = color.Order,
                    };
                    colorIdMap[color.Id] = newColor.Id;
                    newVersion.PaletteColors.Add(newColor);
                }

                foreach (var layer in version.Layers)
                {
                    newVersion.Layers.Add(new Layer
                    {
                        Id = Guid.NewGuid(),
                        VersionId = newVersion.Id,
                        ColorId = colorIdMap[layer.ColorId],
                        Name = layer.Name,
                        Order = layer.Order,
                        Visible = layer.Visible,
                        Locked = layer.Locked,
                        ManufacturingOperation = layer.ManufacturingOperation,
                    });
                }

                newDocument.Versions.Add(newVersion);
            }

            duplicate.VectorDocuments.Add(newDocument);
        }

        // CurrentVersionId se asigna en un SEGUNDO SaveChangesAsync, no en el primer
        // INSERT: Project.CurrentVersionId -> DocumentVersion, DocumentVersion.VectorDocumentId
        // -> VectorDocument y VectorDocument.ProjectId -> Project forman un ciclo real entre
        // tres filas NUEVAS insertadas en el mismo batch (EF Core no puede ordenar un INSERT
        // cíclico y lanza InvalidOperationException: "circular dependency"). Como
        // CurrentVersionId es nullable, se rompe el ciclo insertando primero con
        // CurrentVersionId=null (grafo restante sin ciclos) y recién después, con todas las
        // filas ya existentes, se completa el puntero.
        _dbContext.Projects.Add(duplicate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (duplicateCurrentVersionId is not null)
        {
            duplicate.CurrentVersionId = duplicateCurrentVersionId;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return duplicate;
    }
}
