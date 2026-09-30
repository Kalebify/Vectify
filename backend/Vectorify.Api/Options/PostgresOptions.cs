namespace Vectorify.Api.Options;

/// <summary>
/// Configuración de conexión a PostgreSQL (M2.2-S01), enlazada a la sección "Postgres"
/// de appsettings/variables de entorno (Postgres__ConnectionString) -- MISMO patrón que
/// <see cref="PythonEngineOptions"/>/<see cref="FrontendCorsOptions"/>. A diferencia de
/// esas dos, esta sección NUNCA se resuelve desde appsettings.json ni
/// appsettings.Development.json (contendría credenciales, aunque sean de desarrollo):
/// siempre viene de una variable de entorno -- en Development la inyecta
/// docker-compose.yml (Postgres__ConnectionString), en Test cada test configura la
/// suya propia (Testcontainers) sin pasar por appsettings, y en Production solo por
/// variable de entorno del proceso/orquestador real.
/// Si no está configurada, la API arranca igual: Program.cs omite la migración
/// automática al inicio y el health check reporta Postgres como "unavailable" -- mismo
/// criterio de tolerancia a fallos que ya existe para el motor Python.
/// </summary>
public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    /// <summary>
    /// Cadena de conexión Npgsql completa (Host/Port/Database/Username/Password).
    /// Vacía por defecto a propósito: nunca se hardcodea un valor con credenciales,
    /// ni siquiera de ejemplo, en el código o en appsettings.*.json.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
