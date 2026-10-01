using Microsoft.EntityFrameworkCore;

namespace Vectorify.Api.Data;

/// <summary>
/// DbContext de EF Core/PostgreSQL introducido por M2.2-S01 (primera tarjeta de
/// MVP2.2). Deliberadamente MÍNIMO: "Modelo completo de dominio" está explícitamente
/// fuera de alcance de esta tarjeta (lo trae M2.2-S02, la tarjeta siguiente). El único
/// DbSet (<see cref="SchemaProbe"/>) es un marcador DESCARTABLE cuyo único propósito es
/// demostrar el pipeline completo de punta a punta que pide el DoD (migrar, escribir,
/// releer, sobrevivir a un restart de Postgres) -- se espera que M2.2-S02 lo reemplace
/// por el modelo real de dominio.
///
/// NO modela proyectos/imágenes/paletas/capas como entidades EF: esos siguen viviendo,
/// sin cambios, en los registries de archivos JSON existentes bajo App_Data/
/// (PersistentProjectRegistry, PersistentLayerLayoutVersionRegistry, etc.), que esta
/// tarjeta no toca ni reemplaza.
/// </summary>
public sealed class VectorizationDbContext : DbContext
{
    public VectorizationDbContext(DbContextOptions<VectorizationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SchemaProbe> SchemaProbes => Set<SchemaProbe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SchemaProbe>(entity =>
        {
            entity.ToTable("schema_probes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.CreatedAt).IsRequired();
        });
    }
}

/// <summary>
/// Tabla marcador MÍNIMA, sin ningún significado de dominio -- ver el comentario de
/// <see cref="VectorizationDbContext"/>. Descartable/reemplazable en cuanto M2.2-S02
/// introduzca el modelo real.
/// </summary>
public sealed class SchemaProbe
{
    public int Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
