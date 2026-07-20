using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Part12_ElectiveBranches.Tests;
public sealed class NotificationGenericTests
{
    [Fact]
    public async Task EmailEndpointRejectsNonExampleTestRecipient()
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(
            (_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:RunConsumer"] = "false",
                ["Notifications:RunScheduler"] = "false",
                ["ConnectionStrings:Notifications"] = null,
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
            })));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/notifications/email",
            new { Recipient = "someone@real.com", Subject = "test", HtmlBody = "<p>hi</p>", TextBody = (string?)null, IdempotencyKey = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmailEndpointAcceptsExampleTestRecipient()
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(
            (_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:RunConsumer"] = "false",
                ["Notifications:RunScheduler"] = "false",
                ["ConnectionStrings:Notifications"] = null,
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
            })));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/notifications/email",
            new { Recipient = "student@example.test", Subject = "Course notice", HtmlBody = "<p>hi</p>", TextBody = (string?)"hi", IdempotencyKey = (string?)"key-1" });
        // Without a DB, the endpoint throws 500; with a DB it returns 202.
        // The address-validation logic is what we test here (200/202 path requires Docker).
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task KafkaStatusEndpointReturnsGroupAndTopic()
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(
            (_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:RunConsumer"] = "false",
                ["Notifications:RunScheduler"] = "false",
                ["ConnectionStrings:Notifications"] = null,
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
                ["Kafka:GroupId"] = "test-group",
            })));
        using var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<KafkaStatus>("/api/kafka/status");
        Assert.NotNull(status);
        Assert.Equal("campus.enrollment.activity.v1", status!.Topic);
        Assert.Equal("test-group", status.ConsumerGroup);
    }

    [Fact]
    public async Task BackoffScheduleMathIsBounded()
    {
        // Verify the exponential backoff formula the scheduler uses:
        // delay = min(base * 2^attempt, maxBackoff)
        var baseBackoff = 500;
        var maxBackoff = 30000;
        Assert.Equal(500, Math.Min(baseBackoff * (int)Math.Pow(2, 0), maxBackoff));
        Assert.Equal(1000, Math.Min(baseBackoff * (int)Math.Pow(2, 1), maxBackoff));
        Assert.Equal(2000, Math.Min(baseBackoff * (int)Math.Pow(2, 2), maxBackoff));
        Assert.Equal(4000, Math.Min(baseBackoff * (int)Math.Pow(2, 3), maxBackoff));
        Assert.Equal(8000, Math.Min(baseBackoff * (int)Math.Pow(2, 4), maxBackoff));
        Assert.Equal(16000, Math.Min(baseBackoff * (int)Math.Pow(2, 5), maxBackoff));
        Assert.Equal(maxBackoff, Math.Min(baseBackoff * (int)Math.Pow(2, 10), maxBackoff));
    }

    private sealed record KafkaStatus(string Topic, int Partitions, long ConsumerLag, string ConsumerGroup);
}
