using System.Diagnostics;
using Campus.ServiceDefaults;
using Part08_1_OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);
builder.AddCampusServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient<TroubleshootingClient>((services, client) =>
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        client.BaseAddress = new Uri(
            configuration["Troubleshooting:BaseUrl"] ?? "http://127.0.0.1:6082");
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(50);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
    });

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part08_1_OpenTelemetry",
    signals = new[] { "logs", "metrics", "traces" },
    exporter = "OTLP",
    dashboard = "http://localhost:18888",
    privacy = "No request bodies, credentials, tokens, or user identifiers are recorded.",
}));

app.MapGet("/api/observability/work/{workId:guid}", async (
    Guid workId,
    int? delayMs,
    TroubleshootingClient downstream,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var stopwatch = Stopwatch.StartNew();
    using var activity = CampusTelemetry.ActivitySource.StartActivity(
        "observability.process-work",
        ActivityKind.Internal);
    activity?.SetTag("campus.work.kind", "course-sync");
    activity?.SetTag("campus.work.id", workId);

    using (logger.BeginScope(new Dictionary<string, object?>
    {
        ["WorkId"] = workId,
        ["TraceId"] = Activity.Current?.TraceId.ToString(),
    }))
    {
        logger.LogInformation("Starting instrumented work");
        var result = await downstream.GetWorkAsync(
            workId,
            Math.Clamp(delayMs ?? 25, 0, 2000),
            cancellationToken);
        stopwatch.Stop();

        CampusTelemetry.Operations.Add(
            1,
            new KeyValuePair<string, object?>("operation", "course-sync"),
            new KeyValuePair<string, object?>("outcome", "success"));
        CampusTelemetry.OperationDuration.Record(
            stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("operation", "course-sync"));
        logger.LogInformation(
            "Completed instrumented work in {ElapsedMilliseconds} ms",
            stopwatch.Elapsed.TotalMilliseconds);

        return Results.Ok(new
        {
            workId,
            result,
            traceId = Activity.Current?.TraceId.ToString(),
            spanId = Activity.Current?.SpanId.ToString(),
        });
    }
});

app.MapGet("/api/observability/failure", () =>
{
    using var activity = CampusTelemetry.ActivitySource.StartActivity(
        "observability.expected-failure",
        ActivityKind.Internal);
    CampusTelemetry.Failures.Add(
        1,
        new KeyValuePair<string, object?>("failure", "validation"));
    activity?.SetStatus(ActivityStatusCode.Error, "Controlled lab failure");
    return Results.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Controlled observability failure",
        extensions: new Dictionary<string, object?>
        {
            ["activityTraceId"] = Activity.Current?.TraceId.ToString(),
        });
});

app.MapCampusDefaultEndpoints();
app.Run();

public partial class Program;
