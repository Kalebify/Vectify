using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vectorify.Api.Migrations
{
    /// <summary>
    /// Migración INTENCIONALMENTE vacía (M2.2-S03): el scaffolding de `dotnet ef
    /// migrations add` generó por defecto un `AddColumn`/`DropColumn` para "xmin" sobre
    /// "projects", pero "xmin" es una columna de SISTEMA que PostgreSQL agrega
    /// automáticamente a TODA tabla (contador de versión de fila usado internamente por
    /// MVCC) -- no se puede crear ni borrar con ALTER TABLE: un intento real falla en
    /// runtime con "column name "xmin" conflicts with a system column name". Se removieron
    /// esas dos operaciones a mano; esta migración solo deja registrado en el historial de
    /// EF Core (__EFMigrationsHistory) que, desde este punto, Project.xmin (shadow
    /// property, ver VectorizationDbContext.OnModelCreating) está mapeada como concurrency
    /// token contra esa columna ya existente. Ningún DDL real corre al aplicarla.
    /// </summary>
    public partial class AddProjectConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sin operaciones: "xmin" ya existe como columna de sistema en "projects"
            // desde que la tabla fue creada (InitialCreate) -- ver el comentario de clase.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin operaciones, simétrico a Up(): "xmin" es una columna de sistema que
            // tampoco se puede (ni debe) borrar.
        }
    }
}
