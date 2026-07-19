using System.Text.Json;
using Campus.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Part06_1_MessagingPatterns;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Messaging")
    ?? "Host=localhost;Port=5432;Database=campus_w7_patterns;Username=dotnet;Password=dotnet_dev";
builder.Services.AddDbContext<MessagingDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LabOutboxPublisher>();
builder.Services.AddSingleton<IOutboxPublisher>(services =>
    services.GetRequiredService<LabOutboxPublisher>());
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddScoped<InboxProcessor>();
builder.Services.AddScoped<SagaOrchestrator>();
builder.Services.AddHostedService<OutboxRelay>();
builder.Services.AddHealthChecks()
    .AddCheck<MessagingDatabaseHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = error switch
    {
        InvalidOperationException => (StatusCodes.Status409Conflict, "Invalid state transition"),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected messaging failure"),
    };
    await Results.Problem(
        statusCode: status,
        title: title,
        detail: app.Environment.IsDevelopment() ? error?.Message : null)
        .ExecuteAsync(context);
}));

if (app.Environment.IsDevelopment() ||
    app.Configuration.GetValue("Database:ApplyMigrations", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
    await database.Database.MigrateAsync();
}

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part06_1_MessagingPatterns",
    delivery = "at-least-once",
    effectiveDelivery = "at-least-once + Inbox idempotency",
    patterns = new[]
    {
        "Transactional Outbox",
        "FOR UPDATE SKIP LOCKED",
        "Inbox ON CONFLICT",
        "Retry with exponential backoff and jitter",
        "Dead Letter Queue",
        "Orchestrated Saga with compensation",
    },
}));

var api = app.MapGroup("/api/messaging");

api.MapPost("/enrollments", async (
    CreateEnrollmentCommand request,
    MessagingDbContext database,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    var now = timeProvider.GetUtcNow();
    var enrollment = new EnrollmentRecord
    {
        Id = Guid.NewGuid(),
        StudentId = request.StudentId,
        SectionId = request.SectionId,
        Status = "Pending",
        CreatedOnUtc = now,
    };
    var integrationEvent = new MessageEnvelope<EnrollmentRequestedV1>(
        Guid.NewGuid(),
        IntegrationEventNames.EnrollmentRequested,
        1,
        now,
        enrollment.Id.ToString("N"),
        new EnrollmentRequestedV1(
            enrollment.Id,
            enrollment.StudentId,
            enrollment.SectionId));
    database.Enrollments.Add(enrollment);
    database.OutboxMessages.Add(new OutboxMessage
    {
        Id = integrationEvent.MessageId,
        Type = integrationEvent.Type,
        ContentJson = JsonSerializer.Serialize(integrationEvent),
        OccurredOnUtc = now,
        NextAttemptOnUtc = now,
    });

    try
    {
        // EF wraps both inserts in one local PostgreSQL transaction: no dual write.
        await database.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException)
    {
        return Results.Conflict(new
        {
            errorCode = "enrollment.duplicate",
            message = "The student is already enrolled in this section.",
        });
    }

    return Results.Created(
        $"/api/messaging/enrollments/{enrollment.Id}",
        enrollment);
});

api.MapGet("/enrollments/{id:guid}", async (
    Guid id,
    MessagingDbContext database,
    CancellationToken cancellationToken) =>
{
    var enrollment = await database.Enrollments
        .AsNoTracking()
        .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
    return enrollment is null ? Results.NotFound() : Results.Ok(enrollment);
});

api.MapPost("/outbox", async (
    QueueLabMessageRequest request,
    MessagingDbContext database,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    var now = timeProvider.GetUtcNow();
    var message = new OutboxMessage
    {
        Id = Guid.NewGuid(),
        Type = request.Type,
        ContentJson = JsonSerializer.Serialize(new { request.Payload }),
        OccurredOnUtc = now,
        NextAttemptOnUtc = now,
        FailureMode = request.FailureMode,
        FailuresBeforeSuccess = Math.Max(0, request.FailuresBeforeSuccess),
    };
    database.OutboxMessages.Add(message);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Accepted($"/api/messaging/outbox/{message.Id}", message);
});

api.MapPost("/outbox/dispatch", async (
    int? batchSize,
    bool? crashAfterPublish,
    OutboxDispatcher dispatcher,
    CancellationToken cancellationToken) =>
{
    var result = await dispatcher.DispatchBatchAsync(
        batchSize ?? 20,
        crashAfterPublish ?? false,
        cancellationToken);
    return Results.Ok(result);
});

api.MapGet("/outbox", async (
    MessagingDbContext database,
    CancellationToken cancellationToken) =>
{
    var messages = await database.OutboxMessages
        .AsNoTracking()
        .OrderBy(message => message.OccurredOnUtc)
        .ToListAsync(cancellationToken);
    return Results.Ok(messages);
});

api.MapGet("/published", (LabOutboxPublisher publisher) =>
    Results.Ok(publisher.Published));

api.MapGet("/dead-letters", async (
    MessagingDbContext database,
    CancellationToken cancellationToken) =>
{
    var messages = await database.DeadLetters
        .AsNoTracking()
        .OrderBy(message => message.FailedOnUtc)
        .ToListAsync(cancellationToken);
    return Results.Ok(messages);
});

api.MapPost("/inbox", async (
    ReceiveInboxMessageRequest request,
    InboxProcessor inbox,
    CancellationToken cancellationToken) =>
    Results.Ok(await inbox.ReceiveAsync(request, cancellationToken)));

api.MapGet("/notification-receipts", async (
    MessagingDbContext database,
    CancellationToken cancellationToken) =>
    Results.Ok(await database.NotificationReceipts
        .AsNoTracking()
        .OrderBy(receipt => receipt.CreatedOnUtc)
        .ToListAsync(cancellationToken)));

api.MapPost("/sagas", async (
    StartSagaRequest request,
    SagaOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    var saga = await orchestrator.StartAsync(request.EnrollmentId, cancellationToken);
    return Results.Created($"/api/messaging/sagas/{saga.Id}", saga);
});

api.MapPost("/sagas/{id:guid}/payment", async (
    Guid id,
    SagaStepResult result,
    SagaOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    var saga = await orchestrator.RecordPaymentAsync(id, result, cancellationToken);
    return saga is null ? Results.NotFound() : Results.Ok(saga);
});

api.MapPost("/sagas/{id:guid}/seat", async (
    Guid id,
    SagaStepResult result,
    SagaOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    var saga = await orchestrator.RecordSeatAsync(id, result, cancellationToken);
    return saga is null ? Results.NotFound() : Results.Ok(saga);
});

api.MapPost("/sagas/{id:guid}/compensation-completed", async (
    Guid id,
    SagaOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    var saga = await orchestrator.CompleteCompensationAsync(id, cancellationToken);
    return saga is null ? Results.NotFound() : Results.Ok(saga);
});

api.MapGet("/sagas/{id:guid}", async (
    Guid id,
    MessagingDbContext database,
    CancellationToken cancellationToken) =>
{
    var saga = await database.Sagas
        .AsNoTracking()
        .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
    return saga is null ? Results.NotFound() : Results.Ok(saga);
});

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
