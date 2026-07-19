using RabbitMQ.Client;

namespace Part06_2_MessagingTools;

public static class RabbitMqTopology
{
    public const string EventsExchange = "campus.events";
    public const string CommandsExchange = "campus.commands";
    public const string BroadcastsExchange = "campus.broadcasts";
    public const string RetryExchange = "campus.retry";
    public const string DeadLetterExchange = "campus.dead-letter";
    public const string NotificationsQueue = "campus.notifications.v1";
    public const string RetryQueue = "campus.notifications.retry.v1";
    public const string DeadLetterQueue = "campus.notifications.dlq.v1";
    public const string DirectDemoQueue = "campus.demo.direct";
    public const string FanoutDemoQueue = "campus.demo.fanout";
    public const string TopicDemoQueue = "campus.demo.topic";

    public static async Task DeclareAsync(
        IChannel channel,
        int retryDelayMilliseconds,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            EventsExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            CommandsExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            BroadcastsExchange,
            ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            RetryExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            DeadLetterExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            NotificationsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = "notification.dead",
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            NotificationsQueue,
            EventsExchange,
            "enrollment.*",
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            RetryQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = Math.Max(1, retryDelayMilliseconds),
                ["x-dead-letter-exchange"] = EventsExchange,
                ["x-dead-letter-routing-key"] = "enrollment.confirmed",
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            RetryQueue,
            RetryExchange,
            "notification.retry",
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            DeadLetterQueue,
            DeadLetterExchange,
            "notification.dead",
            cancellationToken: cancellationToken);

        await DeclareDemoQueueAsync(
            channel,
            DirectDemoQueue,
            CommandsExchange,
            "enrollment.reserve",
            cancellationToken);
        await DeclareDemoQueueAsync(
            channel,
            FanoutDemoQueue,
            BroadcastsExchange,
            "",
            cancellationToken);
        await DeclareDemoQueueAsync(
            channel,
            TopicDemoQueue,
            EventsExchange,
            "enrollment.#",
            cancellationToken);
    }

    public static async Task PurgeAsync(
        IChannel channel,
        CancellationToken cancellationToken)
    {
        foreach (var queue in new[]
                 {
                     NotificationsQueue,
                     RetryQueue,
                     DeadLetterQueue,
                     DirectDemoQueue,
                     FanoutDemoQueue,
                     TopicDemoQueue,
                 })
        {
            await channel.QueuePurgeAsync(queue, cancellationToken);
        }
    }

    private static async Task DeclareDemoQueueAsync(
        IChannel channel,
        string queue,
        string exchange,
        string routingKey,
        CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            queue,
            exchange,
            routingKey,
            cancellationToken: cancellationToken);
    }
}
