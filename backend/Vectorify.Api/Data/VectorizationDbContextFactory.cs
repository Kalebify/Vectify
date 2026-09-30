using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vectorify.Api.Data;

/// <summary>
/// Fábrica de SOLO diseño para el tooling de `dotnet ef` (`migrations add`,
/// `database update`) -- nunca se usa en runtime. En runtime, Program.cs registra
/// <see cref="VectorizationDbContext"/> vía DI a partir de
/// <see cref="Vectorify.Api.Options.PostgresOptions"/> (sección "Postgres",
/// Postgres__ConnectionString).
///
/// `dotnet ef migrations add` solo necesita inspeccionar el modelo (no abre una
/// conexión real), así que el connection string de acá nunca se usa para eso. Para
/// `dotnet ef database update` sí hace falta una base real: se resuelve leyendo la
/// MISMA variable de entorno que usa runtime (Postgres__ConnectionString) y, si no
/// está seteada, cae a un default apuntando a localhost -- pensado para correr contra
/// `docker compose up postgres` con el puerto publicado en el host. Nunca hardcodea
/// credenciales reales: el default es el mismo valor de ejemplo no-secreto que
/// documenta .env.example.
/// </summary>
public sealed class VectorizationDbContextFactory : IDesignTimeDbContextFactory<VectorizationDbContext>
{
    public VectorizationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("Postgres__ConnectionString")
            ?? "Host=localhost;Port=5432;Database=vectorify;Username=vectorify;Password=vectorify_dev_password";

        var optionsBuilder = new DbContextOptionsBuilder<VectorizationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new VectorizationDbContext(optionsBuilder.Options);
    }
}
