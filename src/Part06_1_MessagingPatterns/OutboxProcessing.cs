using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Part06_1_MessagingPatterns;

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed record PublishedMessage(
    Guid MessageId,
    string Type,
    string ContentJson,
    DateTimeOffset PublishedOnUtc);

public sealed class LabOutboxPublisher(TimeProvider timeProvider) : IOutboxPublisher
{
    private readonly ConcurrentDictionary<Guid, int> _deliveryAttempts = [];
    private readonly ConcurrentQueue<PublishedMessage> _published = [];

    public IReadOnlyCollection<PublishedMessage> Published => _published.ToArray();

    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = _deliveryAttempts.AddOrUpdate(message.Id, 1, (_, current) => current + 1);
        if (string.Equals(message.FailureMode, "poison", StringComparison.OrdinalIgnoreCase))
        {
            throw new PoisonMessageException("The message was deliberately marked as poison.");
        }

        if (string.Equals(message.FailureMode, "transient", StringComparison.OrdinalIgnoreCase) &&
            attempt <= message.FailuresBeforeSuccess)
        {
            throw new TransientMessageException($"Simulated transient failure {attempt}.");
        }

        _published.Enqueue(new PublishedMessage(
            message.Id,
            message.Type,
            message.ContentJson,
            timeProvider.GetUtcNow()));
        return Task.CompletedTask;
    }
}

public sealed class OutboxDispatcher(
    MessagingDbContext db,
    IOutboxPublisher publisher,
    TimeProvider timeProvider,
    IConfiguration configuration)
{
    private readonly int _maxAttempts = Math.Max(
        1,
        configuration.GetValue("Messaging:MaxDeliveryAttempts", 3));

    public async Task<DispatchResult> DispatchBatchAsync(
        int batchSize,
        bool crashAfterPublish,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(batchSize, 1, 100);
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var messages = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT *
                FROM outbox_messages
                WHERE processed_on_utc IS NULL
                  AND next_attempt_on_utc <= {now}
                ORDER BY occurred_on_utc, id
                LIMIT {take}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var published = 0;
        var retried = 0;
        var deadLettered = 0;
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                published++;
                if (crashAfterPublish)
                {
                    throw new SimulatedRelayCrashException(
                        "Relay stopped after publish and before marking the row.");
                }

                message.ProcessedOnUtc = timeProvider.GetUtcNow();
                message.LastError = null;
            }
            catch (PoisonMessageException ex)
            {
                MoveToDeadLetter(message, ex.Message);
                deadLettered++;
            }
            catch (TransientMessageException ex)
            {
                message.Attempts++;
                message.LastError = ex.Message;
                if (message.Attempts >= _maxAttempts)
                {
                    MoveToDeadLetter(message, ex.Message);
                    deadLettered++;
                }
                else
                {
                    message.NextAttemptOnUtc =
                        timeProvider.GetUtcNow() + RetrySchedule.ExponentialWithJitter(
                            message.Attempts,
                            TimeSpan.FromMilliseconds(20),
                            message.Id);
                    retried++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DispatchResult(messages.Count, published, retried, deadLettered);
    }

    private void MoveToDeadLetter(OutboxMessage message, string reason)
    {
        message.ProcessedOnUtc = timeProvider.GetUtcNow();
        message.LastError = reason;
        db.DeadLetters.Add(new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            OriginalMessageId = message.Id,
            Type = message.Type,
            ContentJson = message.ContentJson,
            Reason = reason,
            FailedOnUtc = timeProvider.GetUtcNow(),
            Attempts = message.Attempts,
        });
    }
}

public static class RetrySchedule
{
    public static TimeSpan ExponentialWithJitter(
        int attempt,
        TimeSpan baseDelay,
        Guid jitterKey)
    {
        var exponent = Math.Min(Math.Max(attempt - 1, 0), 10);
        var exponentialMs = baseDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var keyBytes = jitterKey.ToByteArray();
        var jitter = 0.8 + (keyBytes[0] / 255d * 0.4);
        return TimeSpan.FromMilliseconds(exponentialMs * jitter);
    }
}

public sealed class TransientMessageException(string message) : Exception(message);

public sealed class PoisonMessageException(string message) : Exception(message);

public sealed class SimulatedRelayCrashException(string message) : Exception(message);

public sealed class OutboxRelay(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Messaging:RunRelay", true))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
                await dispatcher.DispatchBatchAsync(20, false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                    TimeoutException or
                    Npgsql.NpgsqlException)
            {
                logger.LogWarning(ex, "Outbox relay iteration failed and will be retried.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
