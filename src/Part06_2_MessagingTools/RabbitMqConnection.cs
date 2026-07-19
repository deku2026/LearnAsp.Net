using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Part06_2_MessagingTools;

public sealed class RabbitMqConnection(
    IConfiguration configuration,
    ILogger<RabbitMqConnection> logger)
    : IHostedService, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private int _disposeState;
    private IConnection? _connection;
    private IChannel? _publisherChannel;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("RabbitMQ")
            ?? "amqp://dotnet:dotnet_dev@localhost:5672/";
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
            ClientProvidedName = "LearnAspNet-W7",
            ConsumerDispatchConcurrency = 1,
        };
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _publisherChannel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        var retryDelay = configuration.GetValue(
            "RabbitMQ:RetryDelayMilliseconds",
            250);
        await RabbitMqTopology.DeclareAsync(
            _publisherChannel,
            retryDelay,
            cancellationToken);
        logger.LogInformation(
            "RabbitMQ connection established and W7 topology provisioned.");
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        CloseAsync(cancellationToken);

    public async Task<IChannel> CreateConsumerChannelAsync(
        CancellationToken cancellationToken)
    {
        var connection = _connection;
        if (connection is null || !connection.IsOpen)
        {
            throw new InvalidOperationException("RabbitMQ connection is not open.");
        }

        return await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: 1),
            cancellationToken);
    }

    public async Task PublishAsync(
        RabbitLabMessage message,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.MessageId.ToString(),
            CorrelationId = message.CorrelationId,
            Type = message.Type,
            Headers = new Dictionary<string, object?>
            {
                ["x-attempt"] = 0L,
            },
        };
        await PublishCoreAsync(
            RabbitMqTopology.EventsExchange,
            "enrollment.confirmed",
            properties,
            body,
            cancellationToken);
    }

    public async Task PublishRetryAsync(
        RabbitLabMessage message,
        int attempt,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.MessageId.ToString(),
            CorrelationId = message.CorrelationId,
            Type = message.Type,
            Headers = new Dictionary<string, object?>
            {
                ["x-attempt"] = (long)attempt,
            },
        };
        await PublishCoreAsync(
            RabbitMqTopology.RetryExchange,
            "notification.retry",
            properties,
            body,
            cancellationToken);
    }

    public async Task PublishDemoAsync(
        string exchange,
        string routingKey,
        string payload,
        CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "text/plain",
            MessageId = Guid.NewGuid().ToString(),
            Type = "campus.demo",
        };
        await PublishCoreAsync(
            exchange,
            routingKey,
            properties,
            System.Text.Encoding.UTF8.GetBytes(payload),
            cancellationToken);
    }

    public async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var channel = _publisherChannel
            ?? throw new InvalidOperationException("RabbitMQ publisher channel is not open.");
        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            await RabbitMqTopology.PurgeAsync(channel, cancellationToken);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public async Task<uint> DeadLetterCountAsync(CancellationToken cancellationToken)
    {
        var channel = _publisherChannel
            ?? throw new InvalidOperationException("RabbitMQ publisher channel is not open.");
        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            return await channel.MessageCountAsync(
                RabbitMqTopology.DeadLetterQueue,
                cancellationToken);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        await CloseAsync(CancellationToken.None);
        _publishLock.Dispose();
        _lifecycleLock.Dispose();
    }

    private async Task PublishCoreAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var channel = _publisherChannel
            ?? throw new InvalidOperationException("RabbitMQ publisher channel is not open.");
        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            // Confirmation tracking makes this await broker ack/nack. mandatory=true
            // also rejects silently unroutable messages.
            await channel.BasicPublishAsync(
                exchange,
                routingKey,
                mandatory: true,
                basicProperties: properties,
                body,
                cancellationToken);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private async Task CloseAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await _publishLock.WaitAsync(cancellationToken);
            try
            {
                // Channels are owned by the connection. Closing the connection
                // tears them down in one coordinated automatic-recovery operation.
                Interlocked.Exchange(ref _publisherChannel, null);
                var connection = Interlocked.Exchange(ref _connection, null);
                await CloseConnectionAsync(connection, cancellationToken);
            }
            finally
            {
                _publishLock.Release();
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task CloseConnectionAsync(
        IConnection? connection,
        CancellationToken cancellationToken)
    {
        if (connection is null)
        {
            return;
        }

        try
        {
            if (connection.IsOpen)
            {
                await connection.CloseAsync(cancellationToken);
            }
        }
        catch (AlreadyClosedException ex)
        {
            logger.LogDebug(ex, "RabbitMQ connection was already closed.");
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogDebug(ex, "RabbitMQ connection was already disposed.");
        }
        finally
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (ObjectDisposedException ex)
            {
                logger.LogDebug(ex, "RabbitMQ connection disposal was repeated.");
            }
        }
    }
}

public sealed record RabbitLabMessage(
    Guid MessageId,
    Guid EnrollmentId,
    string Type,
    string CorrelationId,
    string Payload,
    string FailureMode,
    int FailuresBeforeSuccess);
