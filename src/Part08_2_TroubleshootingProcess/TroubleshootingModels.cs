using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Part08_2_TroubleshootingProcess;

public sealed record DownstreamResult(Guid WorkId, int DelayMs, string Status);

public sealed class MemoryPressureState
{
    private readonly Lock _gate = new();
    private readonly List<byte[]> _retained = [];

    public int RetainedMegabytes
    {
        get
        {
            lock (_gate)
            {
                return _retained.Count;
            }
        }
    }

    public void Retain(int megabytes)
    {
        lock (_gate)
        {
            var remaining = Math.Min(megabytes, 64 - _retained.Count);
            for (var index = 0; index < remaining; index++)
            {
                var buffer = GC.AllocateUninitializedArray<byte>(1024 * 1024);
                buffer[0] = 1;
                _retained.Add(buffer);
            }
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _retained.Clear();
        }

        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Aggressive,
            blocking: true,
            compacting: true);
    }
}

public sealed class TroubleshootingDatabaseHealthCheck(
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Troubleshooting");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Degraded(
                "Troubleshooting database is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (
            exception is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy(
                "Troubleshooting database is unavailable.",
                exception);
        }
    }
}
