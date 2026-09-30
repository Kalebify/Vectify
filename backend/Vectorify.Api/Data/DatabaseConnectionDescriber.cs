using Npgsql;

namespace Vectorify.Api.Data;

/// <summary>
/// Describe una connection string de Postgres para logging SIN exponer usuario ni
/// password -- solo host/puerto/nombre de base de datos. La tarjeta M2.2-S01 exige
/// explícitamente que los logs de conexión/migración nunca expongan la connection
/// string completa.
/// </summary>
public static class DatabaseConnectionDescriber
{
    public static string Describe(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"{builder.Host}:{builder.Port}/{builder.Database}";
        }
        catch (Exception)
        {
            return "(connection string no parseable)";
        }
    }
}
