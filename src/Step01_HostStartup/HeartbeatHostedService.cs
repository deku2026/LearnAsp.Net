namespace Step01_HostStartup;

public sealed class HeartbeatHostedService(ILogger<HeartbeatHostedService> logger) : BackgroundService
{
    private long _tickCount;

    public long TickCount => Interlocked.Read(ref _tickCount);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HeartbeatHostedService started");
        while (!stoppingToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _tickCount);
            logger.LogDebug("Heartbeat tick {Tick}", TickCount);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("HeartbeatHostedService stopping gracefully");
    }
}
