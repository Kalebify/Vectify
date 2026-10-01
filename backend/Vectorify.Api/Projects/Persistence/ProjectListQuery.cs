namespace Vectorify.Api.Projects.Persistence;

/// <summary>
/// Parámetros YA VALIDADOS/resueltos de un listado de proyectos (page/pageSize ya
/// acotados a un máximo razonable, sortBy ya parseado) -- construido por
/// <see cref="Vectorify.Api.ProjectManagement.ProjectService"/>, nunca directamente desde
/// query params crudos del endpoint.
/// </summary>
public sealed record ProjectListQuery(int Page, int PageSize, string? Search, ProjectSortBy SortBy);
