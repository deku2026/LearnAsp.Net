using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Part03_2.BuildingBlocks;

public interface IOutbox
{
    Task EnqueueAsync(string type, object payload, CancellationToken ct = default);
}

public interface IOutboxMessageHandler
{
    string MessageType { get; }
    Task HandleAsync(string payloadJson, CancellationToken ct = default);
}

public sealed class InMemoryOutbox : IOutbox
{
    private readonly ConcurrentQueue<OutboxMessage> _queue = new();

    public Task EnqueueAsync(string type, object payload, CancellationToken ct = default)
    {
        _queue.Enqueue(new OutboxMessage(Guid.NewGuid(), type, JsonSerializer.Serialize(payload), DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public bool TryPeek(out OutboxMessage message) => _queue.TryPeek(out message!);
    public bool TryDequeue(out OutboxMessage message) => _queue.TryDequeue(out message!);
}

public sealed record OutboxMessage(Guid Id, string Type, string PayloadJson, DateTimeOffset OccurredAt);

public sealed class OutboxProcessor(
    InMemoryOutbox outbox,
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (outbox.TryPeek(out var msg))
            {
                var handled = false;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>()
                        .Where(h => string.Equals(h.MessageType, msg.Type, StringComparison.Ordinal))
                        .ToList();
                    if (handlers.Count == 0)
                    {
                        logger.LogWarning("No outbox handler registered for {MessageType}", msg.Type);
                        break;
                    }

                    foreach (var handler in handlers)
                    {
                        await handler.HandleAsync(msg.PayloadJson, stoppingToken);
                    }

                    handled = true;
                }
                catch (JsonException ex)
                {
                    logger.LogError(ex, "Outbox JSON error for {MessageId}", msg.Id);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogError(ex, "Outbox handler error for {MessageId}", msg.Id);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                if (!handled)
                {
                    // Keep the message at the head of the queue. This gives at-least-once
                    // delivery inside the process instead of losing it on the first failure.
                    break;
                }

                _ = outbox.TryDequeue(out _);
            }

            try
            {
                await Task.Delay(50, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}

public static class BuildingBlocksExtensions
{
    public static IServiceCollection AddInProcessOutbox(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryOutbox>();
        services.AddSingleton<IOutbox>(sp => sp.GetRequiredService<InMemoryOutbox>());
        services.AddHostedService<OutboxProcessor>();
        return services;
    }
}
