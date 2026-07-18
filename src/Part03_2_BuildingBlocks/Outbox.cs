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
            while (outbox.TryDequeue(out var msg))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>();
                    var handler = handlers.FirstOrDefault(h => string.Equals(h.MessageType, msg.Type, StringComparison.Ordinal));
                    if (handler is not null)
                    {
                        await handler.HandleAsync(msg.PayloadJson, stoppingToken);
                    }
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
