using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Part06_2_MessagingTools;

public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(connection.IsOpen
            ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
            : HealthCheckResult.Unhealthy("RabbitMQ connection is closed."));
}

public sealed class RabbitStoreHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Messaging")
            ?? "Host=localhost;Port=5432;Database=campus_w7_tools;Username=dotnet;Password=dotnet_dev";
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy("Rabbit Inbox PostgreSQL is reachable.");
        }
        catch (NpgsqlException ex)
        {
            return HealthCheckResult.Unhealthy("Rabbit Inbox PostgreSQL failed.", ex);
        }
    }
}
