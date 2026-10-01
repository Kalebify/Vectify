namespace Vectorify.Api.Contracts;

/// <summary>Body de <c>POST /api/v2/projects</c> (M2.2-S03). Name requerido (validado en ProjectService), Description opcional. OwnerId NUNCA viaja en el body -- se resuelve server-side vía IUserContext.</summary>
public sealed record CreateProjectRequest(string? Name, string? Description);

/// <summary>
/// Body de <c>PATCH /api/v2/projects/{id}</c> (M2.2-S03). Semántica elegida (ver
/// ProjectService/IMPL.md): un campo <c>null</c> significa "sin cambios" -- para vaciar
/// Description explícitamente, enviar <c>""</c> (string vacío, no null).
/// </summary>
public sealed record UpdateProjectRequest(string? Name, string? Description);
