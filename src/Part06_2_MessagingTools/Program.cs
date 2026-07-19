using Campus.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Part06_2_MessagingTools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<RabbitInboxStore>();
builder.Services.AddSingleton<RabbitMqConnection>();
builder.Services.AddSingleton<IHostedService>(services =>
    services.GetRequiredService<RabbitMqConnection>());
builder.Services.AddHostedService<RabbitMqConsumer>();
builder.Services.AddHealthChecks()
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"])
    .AddCheck<RabbitStoreHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    await Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Messaging infrastructure unavailable",
        detail: app.Environment.IsDevelopment() ? error?.Message : null)
        .ExecuteAsync(context);
}));

await app.Services.GetRequiredService<RabbitInboxStore>()
    .InitializeAsync(CancellationToken.None);

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part06_2_MessagingTools",
    broker = "RabbitMQ 4.x",
    client = "RabbitMQ.Client 7.x direct SDK",
    licenseChoice = "No MassTransit dependency; transport is isolated behind a local adapter.",
    semantics = "publisher confirms + manual ack + retry queue + DLX/DLQ + PostgreSQL Inbox",
}));

var api = app.MapGroup("/api/rabbit");

api.MapPost("/messages", async (
    PublishRabbitMessageRequest request,
    RabbitMqConnection rabbit,
    CancellationToken cancellationToken) =>
{
    var message = new RabbitLabMessage(
        request.MessageId ?? Guid.NewGuid(),
        request.EnrollmentId,
        IntegrationEventNames.EnrollmentConfirmedV1Name,
        request.CorrelationId ?? Guid.NewGuid().ToString("N"),
        request.Payload,
        request.FailureMode,
        Math.Max(0, request.FailuresBeforeSuccess));
    await rabbit.PublishAsync(message, cancellationToken);
    return Results.Accepted($"/api/rabbit/messages/{message.MessageId}", message);
});

api.MapPost("/demo/{exchangeType}", async (
    string exchangeType,
    DemoPublishRequest request,
    RabbitMqConnection rabbit,
    CancellationToken cancellationToken) =>
{
    var route = exchangeType.ToLowerInvariant() switch
    {
        "direct" => (RabbitMqTopology.CommandsExchange, "enrollment.reserve"),
        "fanout" => (RabbitMqTopology.BroadcastsExchange, ""),
        "topic" => (RabbitMqTopology.EventsExchange, request.RoutingKey ?? "enrollment.demo"),
        _ => default,
    };
    if (string.IsNullOrWhiteSpace(route.Item1))
    {
        return Results.BadRequest(new
        {
            errorCode = "rabbit.exchange_type_invalid",
            allowed = new[] { "direct", "fanout", "topic" },
        });
    }

    await rabbit.PublishDemoAsync(
        route.Item1,
        route.Item2,
        request.Payload,
        cancellationToken);
    return Results.Accepted();
});

api.MapGet("/notifications", async (
    RabbitInboxStore store,
    CancellationToken cancellationToken) =>
    Results.Ok(await store.ListAsync(cancellationToken)));

api.MapGet("/dead-letters/count", async (
    RabbitMqConnection rabbit,
    CancellationToken cancellationToken) =>
    Results.Ok(new
    {
        count = await rabbit.DeadLetterCountAsync(cancellationToken),
    }));

api.MapPost("/purge", async (
    RabbitMqConnection rabbit,
    RabbitInboxStore store,
    CancellationToken cancellationToken) =>
{
    await rabbit.PurgeAsync(cancellationToken);
    await store.ResetAsync(cancellationToken);
    return Results.NoContent();
});

api.MapGet("/topology", (IConfiguration configuration) => Results.Ok(new
{
    exchanges = new[]
    {
        new { name = RabbitMqTopology.EventsExchange, type = "topic" },
        new { name = RabbitMqTopology.CommandsExchange, type = "direct" },
        new { name = RabbitMqTopology.BroadcastsExchange, type = "fanout" },
        new { name = RabbitMqTopology.DeadLetterExchange, type = "direct" },
    },
    queue = RabbitMqTopology.NotificationsQueue,
    retryQueue = RabbitMqTopology.RetryQueue,
    deadLetterQueue = RabbitMqTopology.DeadLetterQueue,
    prefetch = configuration.GetValue("RabbitMQ:Prefetch", 8),
    acknowledgement = "manual",
    delivery = "at-least-once",
}));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();

public partial class Program;

public sealed record PublishRabbitMessageRequest(
    Guid EnrollmentId,
    string Payload,
    Guid? MessageId = null,
    string? CorrelationId = null,
    string FailureMode = "none",
    int FailuresBeforeSuccess = 0);

public sealed record DemoPublishRequest(string Payload, string? RoutingKey = null);
