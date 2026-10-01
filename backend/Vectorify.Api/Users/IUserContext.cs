namespace Vectorify.Api.Users;

/// <summary>
/// Abstracción mínima del "usuario efectivo de esta request" (M2.2-S03): lo único que
/// <see cref="Vectorify.Api.ProjectManagement.ProjectService"/>/
/// <see cref="Vectorify.Api.Projects.Persistence.IProjectRepository"/> necesitan para que
/// el filtrado por <c>Project.OwnerId</c> sea REAL (nunca un no-op). Esta tarjeta NO
/// implementa Auth real -- eso es <c>M2.2-S09 · User Ownership + DevelopmentUserContext</c>
/// (tarjeta posterior), que reemplaza/extiende <see cref="DevelopmentUserContext"/> sin que
/// el resto del código tenga que cambiar: todo depende de ESTA interfaz, nunca de la
/// implementación concreta.
/// </summary>
public interface IUserContext
{
    /// <summary>Id del usuario dueño de la request actual.</summary>
    Guid GetEffectiveUserId();
}
