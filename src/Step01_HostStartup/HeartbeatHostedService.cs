namespace Step01_HostStartup;

public sealed class HeartbeatHostedService(
    ILogger<HeartbeatHostedService> logger,
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    private readonly object _stateLock = new();
    private long _tickCount;
    private Guid? _lastScopedId;

    public long TickCount => Interlocked.Read(ref _tickCount);
    public Guid? LastScopedId
    {
        get
        {
            lock (_stateLock)
            {
                return _lastScopedId;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        lifetime.ApplicationStarted.Register(() => logger.LogInformation("Application started"));
        lifetime.ApplicationStopping.Register(() => logger.LogInformation("Application stopping"));
        lifetime.ApplicationStopped.Register(() => logger.LogInformation("Application stopped"));
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
                    lock (_stateLock)
                    {
                        _lastScopedId = recorder.InstanceId;
                    }

                    logger.LogDebug("Scoped TickRecorder {Id}", recorder.InstanceId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
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
