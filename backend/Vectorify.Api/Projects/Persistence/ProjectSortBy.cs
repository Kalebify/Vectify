namespace Vectorify.Api.Projects.Persistence;

/// <summary>Órdenes soportados por <c>GET /api/v2/projects</c> (M2.2-S03).</summary>
public enum ProjectSortBy
{
    /// <summary>Más recientemente modificado primero (UpdatedAt descendente) -- default.</summary>
    LastModified,

    /// <summary>Alfabético ascendente por Name.</summary>
    Name,

    /// <summary>Más recientemente creado primero (CreatedAt descendente).</summary>
    Created,
}
