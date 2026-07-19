using System.Net;
using System.Net.Http.Json;

namespace Part06_2_MessagingTools.Tests;

[Collection(MessagingToolsCollection.Name)]
[Trait("Category", "Docker")]
public sealed class MessagingToolsTests(MessagingToolsFixture fixture)
{
    [Fact]
    public async Task TopicMessageIsConfirmedConsumedAndPersisted()
    {
        SkipIfUnavailable();
        await using var factory = fixture.CreateFactory();
        using var client = await CreateReadyClientAsync(factory);
        var enrollmentId = Guid.NewGuid();

        using var publish = await client.PostAsJsonAsync(
            "/api/rabbit/messages",
            new PublishRabbitMessageRequest(enrollmentId, "confirmed"));

        Assert.Equal(HttpStatusCode.Accepted, publish.StatusCode);
        var notification = await WaitForNotificationAsync(client);
        Assert.Equal(enrollmentId, notification.EnrollmentId);
        Assert.Equal(0, notification.DeliveryAttempt);
    }

    [Fact]
    public async Task DuplicateDeliveryProducesOnePostgresSideEffect()
    {
        SkipIfUnavailable();
        await using var factory = fixture.CreateFactory();
        using var client = await CreateReadyClientAsync(factory);
        var messageId = Guid.NewGuid();
        var request = new PublishRabbitMessageRequest(
            Guid.NewGuid(),
            "duplicate",
            messageId);

        using var firstPublish = await client.PostAsJsonAsync(
            "/api/rabbit/messages",
            request);
        firstPublish.EnsureSuccessStatusCode();
        using var secondPublish = await client.PostAsJsonAsync(
            "/api/rabbit/messages",
            request);
        secondPublish.EnsureSuccessStatusCode();

        await WaitForNotificationAsync(client);
        await Task.Delay(200);
        var notifications = await client.GetFromJsonAsync<List<RabbitNotification>>(
            "/api/rabbit/notifications");
        var notification = Assert.Single(notifications!);
        Assert.Equal(messageId, notification.MessageId);
    }

    [Fact]
    public async Task TransientFailureUsesRetryQueueBeforeAcknowledgement()
    {
        SkipIfUnavailable();
        await using var factory = fixture.CreateFactory();
        using var client = await CreateReadyClientAsync(factory);

        using var publish = await client.PostAsJsonAsync(
            "/api/rabbit/messages",
            new PublishRabbitMessageRequest(
                Guid.NewGuid(),
                "eventually succeeds",
                FailureMode: "transient",
                FailuresBeforeSuccess: 2));
        publish.EnsureSuccessStatusCode();

        var notification = await WaitForNotificationAsync(client);
        Assert.Equal(2, notification.DeliveryAttempt);
        Assert.Equal("eventually succeeds", notification.Payload);
    }

    [Fact]
    public async Task PoisonMessageIsRejectedToDeadLetterQueue()
    {
        SkipIfUnavailable();
        await using var factory = fixture.CreateFactory();
        using var client = await CreateReadyClientAsync(factory);

        using var publish = await client.PostAsJsonAsync(
            "/api/rabbit/messages",
            new PublishRabbitMessageRequest(
                Guid.NewGuid(),
                "bad payload",
                FailureMode: "poison"));
        publish.EnsureSuccessStatusCode();

        var count = await WaitForDeadLetterAsync(client);
        Assert.Equal(1, count);
        var notifications = await client.GetFromJsonAsync<List<RabbitNotification>>(
            "/api/rabbit/notifications");
        Assert.Empty(notifications!);
    }

    [Fact]
    public async Task DirectFanoutAndTopicExchangesAreReallyRoutable()
    {
        SkipIfUnavailable();
        await using var factory = fixture.CreateFactory();
        using var client = await CreateReadyClientAsync(factory);
        foreach (var exchange in new[] { "direct", "fanout", "topic" })
        {
            using var response = await client.PostAsJsonAsync(
                $"/api/rabbit/demo/{exchange}",
                new DemoPublishRequest($"hello-{exchange}", "enrollment.demo"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var topology = await client.GetFromJsonAsync<TopologyResponse>(
            "/api/rabbit/topology");
        Assert.Equal(4, topology!.Prefetch);
        Assert.Equal("manual", topology.Acknowledgement);
        Assert.Contains(topology.Exchanges, exchange => exchange.Type == "direct");
        Assert.Contains(topology.Exchanges, exchange => exchange.Type == "fanout");
        Assert.Contains(topology.Exchanges, exchange => exchange.Type == "topic");
    }

    private static async Task<HttpClient> CreateReadyClientAsync(
        W7ToolsFactory factory)
    {
        var client = factory.CreateClient();
        try
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                using var ready = await client.GetAsync("/health/ready");
                if (ready.IsSuccessStatusCode)
                {
                    using var purge = await client.PostAsync("/api/rabbit/purge", null);
                    purge.EnsureSuccessStatusCode();
                    return client;
                }

                await Task.Delay(100);
            }

            throw new TimeoutException("RabbitMQ lab did not become ready.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<RabbitNotification> WaitForNotificationAsync(
        HttpClient client)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var notifications = await client.GetFromJsonAsync<List<RabbitNotification>>(
                "/api/rabbit/notifications");
            if (notifications?.Count > 0)
            {
                return notifications[0];
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("RabbitMQ notification was not consumed.");
    }

    private static async Task<int> WaitForDeadLetterAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var response = await client.GetFromJsonAsync<DeadLetterCount>(
                "/api/rabbit/dead-letters/count");
            if (response?.Count > 0)
            {
                return response.Count;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Poison message did not reach the DLQ.");
    }

    private void SkipIfUnavailable()
    {
        Assert.SkipWhen(
            !fixture.IsAvailable,
            fixture.SkipReason ?? "RabbitMQ/PostgreSQL unavailable.");
    }

    private sealed record DeadLetterCount(int Count);

    private sealed record TopologyResponse(
        IReadOnlyList<ExchangeInfo> Exchanges,
        int Prefetch,
        string Acknowledgement);

    private sealed record ExchangeInfo(string Name, string Type);
}
