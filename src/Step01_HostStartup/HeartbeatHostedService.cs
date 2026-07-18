namespace Step01_HostStartup;

public sealed class HeartbeatHostedService(
    ILogger<HeartbeatHostedService> logger,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private long _tickCount;
    private Guid? _lastScopedId;

    public long TickCount => Interlocked.Read(ref _tickCount);
    public Guid? LastScopedId => _lastScopedId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HeartbeatHostedService started");

        // PeriodicTimer (preferred over Task.Delay loop): one tick-per-interval, no drift.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Per-tick work body wrapped in try/catch so a thrown exception
                // doesn't tear down the entire hosted service.
                try
                {
                    Interlocked.Increment(ref _tickCount);
                    logger.LogDebug("Heartbeat tick {Tick}", TickCount);

                    // Pitfall demo: resolving a scoped service from a singleton hosted service
                    // must go through IServiceScopeFactory — never inject scoped directly.
                    using var scope = scopeFactory.CreateScope();
                    var recorder = scope.ServiceProvider.GetRequiredService<TickRecorder>();
                    _lastScopedId = recorder.InstanceId;
                    logger.LogDebug("Scoped TickRecorder {Id}", recorder.InstanceId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Heartbeat tick {Tick} work threw; continuing", TickCount);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }

        logger.LogInformation("HeartbeatHostedService stopping gracefully");
    }
}

/// <summary>Scoped service resolved per-tick to demonstrate IServiceScopeFactory usage.</summary>
public sealed class TickRecorder
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}
