using System.Collections.Concurrent;
using System.Text.Json;
using Part03_2_AppArchitecture.Modules.Enrollment;
using Part03_2_AppArchitecture.Modules.Notices;

namespace Part03_2_AppArchitecture.Outbox;

public interface IOutbox
{
    Task EnqueueAsync<T>(string type, T payload, CancellationToken ct = default);
}

public sealed class InMemoryOutbox : IOutbox
{
    private readonly ConcurrentQueue<OutboxMessage> _queue = new();

    public Task EnqueueAsync<T>(string type, T payload, CancellationToken ct = default)
    {
        _queue.Enqueue(new OutboxMessage(Guid.NewGuid(), type, JsonSerializer.Serialize(payload), DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public bool TryDequeue(out OutboxMessage message) => _queue.TryDequeue(out message!);
}

public sealed record OutboxMessage(Guid Id, string Type, string PayloadJson, DateTimeOffset OccurredAt);

public sealed class OutboxProcessor(InMemoryOutbox outbox, INoticesModule notices, ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (outbox.TryDequeue(out var msg))
            {
                try
                {
                    if (msg.Type == nameof(EnrollmentConfirmed))
                    {
                        var evt = JsonSerializer.Deserialize<EnrollmentConfirmed>(msg.PayloadJson)!;
                        await notices.HandleEnrollmentConfirmedAsync(evt.EnrollmentId, evt.StudentId, evt.SectionId, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Outbox processing failed for {MessageId}", msg.Id);
                }
            }

            await Task.Delay(50, stoppingToken);
        }
    }
}
