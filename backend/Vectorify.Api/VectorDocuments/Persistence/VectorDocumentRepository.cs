using Microsoft.EntityFrameworkCore;
using Vectorify.Api.Data;

namespace Vectorify.Api.VectorDocuments.Persistence;

/// <summary>Implementación EF Core de <see cref="IVectorDocumentRepository"/> (M2.2-S05). Ver la interfaz para el rol en la arquitectura.</summary>
public sealed class VectorDocumentRepository : IVectorDocumentRepository
{
    private readonly VectorizationDbContext _dbContext;

    public VectorDocumentRepository(VectorizationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<VectorDocumentSaveOutcome?> SaveAsync(
        Guid projectId, Guid ownerId, DocumentSnapshot snapshot, CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects
            .Include(p => p.VectorDocuments).ThenInclude(d => d.Versions).ThenInclude(v => v.Layers)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == ownerId, cancellationToken);

        if (project is null)
        {
            return null;
        }

        // Transacción EXPLÍCITA (no el default de UN SaveChangesAsync implícito): el ciclo real
        // Project <-> VectorDocument <-> DocumentVersion (Project.CurrentVersionId -> DocumentVersion
        // nueva, DocumentVersion.VectorDocumentId -> VectorDocument, VectorDocument.ProjectId ->
        // Project) obliga a partir la escritura en DOS SaveChangesAsync -- mismo patrón exacto que
        // ProjectRepository.DuplicateAsync: primero se insertan VectorDocument/DocumentVersion/
        // Layer/PaletteColor con Project.CurrentVersionId todavía intacto (grafo sin ciclo), y
        // recién en un segundo SaveChangesAsync se repunta Project.CurrentVersionId a la versión ya
        // persistida. Sin una transacción EXPLÍCITA envolviendo ambos, un fallo entre el primer y
        // el segundo SaveChangesAsync dejaría el primero ya comprometido -- justo lo que
        // spec.md M2.2-S05 ("Tests": "sin filas a medias") prohíbe. Se hace rollback automático al
        // Dispose si nunca se llama a CommitAsync (p. ej. si el segundo SaveChangesAsync lanza
        // DbUpdateConcurrencyException).
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Un Project v2 tiene, en esta tarjeta, a lo sumo UN VectorDocument (el "documento" del
        // proyecto) -- si todavía no existe (primer Save de este Project), se crea acá.
        var document = project.VectorDocuments.FirstOrDefault();
        if (document is null)
        {
            document = new VectorDocument { Id = Guid.NewGuid(), ProjectId = project.Id };
            project.VectorDocuments.Add(document);

            // Explícito a propósito: VectorDocument llega al change tracker vía fixup de la
            // colección de navegación de un Project NO nuevo (Unchanged) -- sin este Add
            // explícito, EF Core asume (por tener ya un Id de Guid NO default, asignado por la
            // aplicación) que la fila YA EXISTE y genera un UPDATE en vez de un INSERT, que
            // afecta 0 filas y dispara DbUpdateConcurrencyException. Mismo gotcha aplica a
            // DocumentVersion/PaletteColor/Layer más abajo -- ver el reporte del sprint.
            _dbContext.Add(document);
        }

        document.WidthMm = snapshot.WidthMm;
        document.HeightMm = snapshot.HeightMm;
        document.ViewBox = snapshot.ViewBox;
        document.SchemaVersion = snapshot.SchemaVersion;

        var nextVersionNumber = document.Versions.Count == 0
            ? 1
            : document.Versions.Max(v => v.VersionNumber) + 1;

        var now = DateTimeOffset.UtcNow;
        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            VectorDocumentId = document.Id,
            VersionNumber = nextVersionNumber,
            Origin = snapshot.Origin,
            MetadataJson = snapshot.MetadataJson,
            CreatedAt = now,
        };
        document.Versions.Add(version);
        _dbContext.Add(version); // siempre nuevo -- ver comentario de arriba sobre VectorDocument.

        // "IDs estables de Layer" (spec.md M2.2-S05): Layer.Id reutiliza el groupId clásico
        // VERBATIM en cada versión nueva -- pero Layer.Id es la PK GLOBAL de la tabla "layers"
        // (una sola fila puede existir con ese Id en TODA la base, nunca una por VersionId), así
        // que un segundo Save real con el mismo groupId no puede volver a INSERTAR una fila con
        // ese mismo Id sin violar esa PK. Se resuelve con upsert-por-Id: si el groupId YA
        // existía en una versión anterior de este MISMO VectorDocument, esa fila se REUTILIZA
        // (se actualiza in-place y se repunta a la versión nueva) en vez de insertar una
        // segunda -- conserva la identidad estable que pide el criterio de aceptación sin violar
        // el esquema. Efecto secundario documentado (ver reporte del sprint): una vez que una
        // versión posterior "adopta" un Layer, la versión anterior deja de tener esa fila entre
        // sus hijos -- aceptable en esta tarjeta porque GET .../document solo lee la versión
        // ACTUAL (Project.CurrentVersionId), nunca el historial completo de versiones viejas.
        var existingLayersById = document.Versions
            .SelectMany(v => v.Layers)
            .ToDictionary(l => l.Id);

        foreach (var layerSnapshot in snapshot.Layers)
        {
            var color = new PaletteColor
            {
                Id = Guid.NewGuid(),
                VersionId = version.Id,
                Hex = layerSnapshot.Color.Hex,
                Coverage = layerSnapshot.Color.Coverage,
                IsBackground = layerSnapshot.Color.IsBackground,
                Order = layerSnapshot.Color.Order,
            };
            version.PaletteColors.Add(color);
            _dbContext.Add(color); // siempre nuevo -- ver comentario sobre VectorDocument más arriba.

            if (existingLayersById.TryGetValue(layerSnapshot.LayerId, out var existingLayer))
            {
                existingLayer.VersionId = version.Id;
                existingLayer.ColorId = color.Id;
                existingLayer.Name = layerSnapshot.Name;
                existingLayer.Order = layerSnapshot.Order;
                existingLayer.Visible = layerSnapshot.Visible;
                existingLayer.Locked = layerSnapshot.Locked;
                existingLayer.ManufacturingOperation = layerSnapshot.ManufacturingOperation;
                existingLayer.SvgAssetId = layerSnapshot.SvgAssetId;
                existingLayer.PathCount = layerSnapshot.PathCount;
            }
            else
            {
                var newLayer = new Layer
                {
                    Id = layerSnapshot.LayerId,
                    VersionId = version.Id,
                    ColorId = color.Id,
                    Name = layerSnapshot.Name,
                    Order = layerSnapshot.Order,
                    Visible = layerSnapshot.Visible,
                    Locked = layerSnapshot.Locked,
                    ManufacturingOperation = layerSnapshot.ManufacturingOperation,
                    SvgAssetId = layerSnapshot.SvgAssetId,
                    PathCount = layerSnapshot.PathCount,
                };
                version.Layers.Add(newLayer);
                _dbContext.Add(newLayer); // siempre nuevo -- ver comentario sobre VectorDocument más arriba.
            }
        }

        // Fase 1: inserta VectorDocument/DocumentVersion/Layer/PaletteColor -- Project.CurrentVersionId
        // todavía no se tocó, así que este grafo no tiene ciclos (ver comentario de arriba).
        await _dbContext.SaveChangesAsync(cancellationToken);

        project.CurrentVersionId = version.Id;
        project.UpdatedAt = now;

        // Fase 2: repunta Project a la versión recién persistida. Acá es donde EF Core emite el
        // UPDATE real sobre "projects" (con xmin en el WHERE) -- si hubo un Save concurrente entre
        // el FirstOrDefaultAsync de arriba y este punto, lanza DbUpdateConcurrencyException
        // (VectorDocumentService la traduce a VectorDocumentResult.Conflict, 409).
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new VectorDocumentSaveOutcome(project.Id, version.VersionNumber, project.UpdatedAt);
    }

    public async Task<(VectorDocument Document, DocumentVersion Version)?> FindCurrentDocumentAsync(
        Guid projectId, Guid ownerId, CancellationToken cancellationToken)
    {
        var project = await _dbContext.Projects
            .Include(p => p.VectorDocuments)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == ownerId, cancellationToken);

        if (project?.CurrentVersionId is null)
        {
            return null;
        }

        var version = await _dbContext.DocumentVersions
            .Include(v => v.Layers).ThenInclude(l => l.Color)
            .Include(v => v.PaletteColors)
            .FirstOrDefaultAsync(v => v.Id == project.CurrentVersionId, cancellationToken);

        if (version is null)
        {
            return null;
        }

        var document = project.VectorDocuments.First(d => d.Id == version.VectorDocumentId);
        return (document, version);
    }

    public async Task<Layer?> UpdateLayerAsync(
        Guid projectId, Guid ownerId, Guid layerId, LayerPatch patch, CancellationToken cancellationToken)
    {
        var layer = await _dbContext.Layers
            .Include(l => l.Version).ThenInclude(v => v!.VectorDocument)
            .FirstOrDefaultAsync(l => l.Id == layerId, cancellationToken);

        if (layer?.Version?.VectorDocument is null)
        {
            return null;
        }

        var project = await _dbContext.Projects.FirstOrDefaultAsync(
            p => p.Id == projectId && p.Id == layer.Version.VectorDocument.ProjectId && p.OwnerId == ownerId,
            cancellationToken);

        // 404 uniforme: el proyecto de la ruta no coincide con el dueño real del layer, O no
        // pertenece al usuario efectivo, O el layer pertenece a una versión histórica ya
        // superada (solo la versión ACTUAL del proyecto es editable vía este endpoint, ver
        // IVectorDocumentRepository).
        if (project is null || project.CurrentVersionId != layer.VersionId)
        {
            return null;
        }

        if (patch.Name is not null)
        {
            layer.Name = patch.Name;
        }

        if (patch.Order is not null)
        {
            layer.Order = patch.Order.Value;
        }

        if (patch.Visible is not null)
        {
            layer.Visible = patch.Visible.Value;
        }

        if (patch.Locked is not null)
        {
            layer.Locked = patch.Locked.Value;
        }

        if (patch.TouchOperation)
        {
            layer.ManufacturingOperation = patch.Operation;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return layer;
    }
}
