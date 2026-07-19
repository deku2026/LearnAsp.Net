using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Part06_1_MessagingPatterns;

public sealed class MessagingDatabaseHealthCheck(
    IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL rejected the readiness probe.");
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
                TimeoutException or
                Npgsql.NpgsqlException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness failed.", ex);
        }
    }
}
