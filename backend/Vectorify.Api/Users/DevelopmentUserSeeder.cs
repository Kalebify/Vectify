using Microsoft.EntityFrameworkCore;
using Vectorify.Api.Data;

namespace Vectorify.Api.Users;

/// <summary>
/// Siembra, al ARRANCAR la API (ver Program.cs, mismo punto donde se aplican las
/// migraciones), el <see cref="User"/> fijo que <see cref="DevelopmentUserContext"/>
/// devuelve siempre -- así la FK <c>Project.OwnerId -&gt; Users.Id</c> nunca falla al crear
/// el primer proyecto de una sesión de desarrollo nueva. Idempotente: no hace nada si la
/// fila ya existe (reinicios sucesivos del proceso contra la misma base).
/// </summary>
public static class DevelopmentUserSeeder
{
    public static async Task EnsureSeededAsync(VectorizationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.Users
            .AnyAsync(u => u.Id == DevelopmentUserContext.DevelopmentUserId, cancellationToken);

        if (exists)
        {
            return;
        }

        dbContext.Users.Add(new User
        {
            Id = DevelopmentUserContext.DevelopmentUserId,
            DisplayName = "Dev User",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
