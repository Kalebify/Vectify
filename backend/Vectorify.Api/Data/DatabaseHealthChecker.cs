using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vectorify.Api.Options;

namespace Vectorify.Api.Data;

/// <summary>Estado de PostgreSQL tal como lo observó la Web API al chequear la conexión.</summary>
public enum DatabaseHealthState
{
    Online,
    Unavailable,
    Error,
}

public sealed record DatabaseHealthCheckResult(DatabaseHealthState State, string? Message);

/// <summary>
/// Chequeo de salud de PostgreSQL para GET /api/v1/system/health -- mismo criterio de
/// tolerancia a fallos que <c>IPythonVectorizationClient.CheckHealthAsync</c>: nunca
/// deja escapar una excepción no controlada, para que el endpoint compuesto siga
/// respondiendo 200 aunque Postgres esté caído.
/// </summary>
public interface IDatabaseHealthChecker
{
    Task<DatabaseHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class DatabaseHealthChecker : IDatabaseHealthChecker
{
    private readonly VectorizationDbContext _dbContext;
    private readonly IOptions<PostgresOptions> _options;
    private readonly ILogger<DatabaseHealthChecker> _logger;

    public DatabaseHealthChecker(
        VectorizationDbContext dbContext,
        IOptions<PostgresOptions> options,
        ILogger<DatabaseHealthChecker> logger)
    {
        _dbContext = dbContext;
        _options = options;
        _logger = logger;
    }

    public async Task<DatabaseHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.ConnectionString))
        {
            return new DatabaseHealthCheckResult(
                DatabaseHealthState.Unavailable,
                "Postgres:ConnectionString no está configurado.");
        }

        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? new DatabaseHealthCheckResult(DatabaseHealthState.Online, null)
                : new DatabaseHealthCheckResult(
                    DatabaseHealthState.Unavailable,
                    "No se pudo establecer conexión con PostgreSQL.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout al verificar la conexión a PostgreSQL.");
            return new DatabaseHealthCheckResult(
                DatabaseHealthState.Unavailable,
                "Tiempo de espera agotado al contactar PostgreSQL.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error inesperado al verificar la conexión a PostgreSQL.");
            return new DatabaseHealthCheckResult(
                DatabaseHealthState.Error,
                "Error inesperado al verificar la conexión a PostgreSQL.");
        }
    }
}
