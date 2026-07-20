using System.Text.Json;
using Confluent.Kafka;

namespace Part12_ElectiveBranches;

public sealed class KafkaConsumerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly KafkaProducerService _producer;
    private readonly InboxStore _inbox;
    private IConsumer<string, string>? _consumer;
    private int _disposeState;

    public KafkaConsumerService(
        IConfiguration configuration,
        ILogger<KafkaConsumerService> logger,
        KafkaProducerService producer,
        InboxStore inbox)
    {
        _configuration = configuration;
        _logger = logger;
        _producer = producer;
        _inbox = inbox;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runConsumer = _configuration.GetValue("Kafka:RunConsumer", true);
        if (!runConsumer)
        {
            return Task.CompletedTask;
        }
        return ConsumeLoopAsync(stoppingToken);
    }

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var bootstrap = _configuration.GetConnectionString("Kafka") ?? "localhost:9094";
        var groupId = _configuration["Kafka:GroupId"] ?? "campus-w9-consumer";
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = groupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = true,
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(KafkaTopics.EnrollmentActivity);
        _logger.LogInformation("Kafka consumer started for {Topic} group {Group}", KafkaTopics.EnrollmentActivity, groupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = _consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error");
                    continue;
                }

                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

                await ProcessMessageAsync(result, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _consumer.Close();
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        EnrollmentActivityEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize<EnrollmentActivityEvent>(result.Message.Value);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Kafka message, routing to DLQ");
            await _producer.PublishToDlqAsync(
                new EnrollmentActivityEvent(Guid.Empty, Guid.Empty, Guid.Empty, "poison", DateTimeOffset.UtcNow),
                $"deserialization: {ex.Message}",
                cancellationToken);
            _consumer!.Commit(result);
            return;
        }

        if (evt is null || evt.EnrollmentId == Guid.Empty)
        {
            await _producer.PublishToDlqAsync(
                evt ?? new EnrollmentActivityEvent(Guid.Empty, Guid.Empty, Guid.Empty, "poison", DateTimeOffset.UtcNow),
                "empty or invalid enrollment id",
                cancellationToken);
            _consumer!.Commit(result);
            return;
        }

        var processed = await _inbox.MarkProcessedAsync(evt.EnrollmentId, cancellationToken);
        if (!processed)
        {
            _logger.LogInformation("Enrollment activity {EnrollmentId} already processed (idempotent inbox)", evt.EnrollmentId);
        }
        else
        {
            _logger.LogInformation("Processed enrollment activity {EnrollmentId}", evt.EnrollmentId);
        }
        _consumer!.Commit(result);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }
        _consumer?.Dispose();
        return ValueTask.CompletedTask;
    }
}