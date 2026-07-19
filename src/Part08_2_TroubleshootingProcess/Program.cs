using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Campus.ServiceDefaults;
using Npgsql;
using Part08_2_TroubleshootingProcess;

var builder = WebApplication.CreateBuilder(args);
builder.AddCampusServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<MemoryPressureState>();
builder.Services.AddHealthChecks()
    .AddCheck<TroubleshootingDatabaseHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part08_2_TroubleshootingProcess",
    workflow = new[]
    {
        "1. Metrics: quantify scope and start time",
        "2. Traces: locate the slow or failing span",
        "3. Logs: correlate structured events by trace ID",
        "4. Database: inspect waits, pools, plans, and query count",
        "5. Runtime: collect counters, stacks, traces, dumps, and GC dumps",
        "6. Deployment: compare revision, configuration, probes, and rollback",
    },
    safety = "Fault endpoints are disabled by default and require a lab token.",
}));

app.MapGet("/api/downstream/{workId:guid}", async (
    Guid workId,
    int? delayMs,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var boundedDelay = Math.Clamp(delayMs ?? 25, 0, 2000);
    using var activity = CampusTelemetry.ActivitySource.StartActivity(
        "troubleshooting.downstream-work",
        ActivityKind.Internal);
    activity?.SetTag("campus.delay.bucket", DelayBucket(boundedDelay));
    await Task.Delay(boundedDelay, cancellationToken);
    logger.LogInformation(
        "Downstream work completed after {DelayMilliseconds} ms",
        boundedDelay);
    return Results.Ok(new DownstreamResult(workId, boundedDelay, "completed"));
});

var lab = app.MapGroup("/lab")
    .AddEndpointFilter(async (context, next) =>
    {
        var httpContext = context.HttpContext;
        var configuration = httpContext.RequestServices
            .GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("Troubleshooting:FaultInjectionEnabled", false))
        {
            return Results.NotFound();
        }

        var expected = configuration["Troubleshooting:LabToken"];
        var supplied = httpContext.Request.Headers["X-Lab-Token"].ToString();
        if (!ConstantTimeEquals(expected, supplied))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    });

lab.MapGet("/slow", async (
    int? delayMs,
    CancellationToken cancellationToken) =>
{
    var boundedDelay = Math.Clamp(delayMs ?? 1000, 1, 5000);
    await Task.Delay(boundedDelay, cancellationToken);
    return Results.Ok(new { scenario = "slow-downstream", boundedDelay });
});

lab.MapGet("/cpu", (int? milliseconds) =>
{
    var duration = Math.Clamp(milliseconds ?? 500, 10, 3000);
    var stopwatch = Stopwatch.StartNew();
    var iterations = 0L;
    while (stopwatch.ElapsedMilliseconds < duration)
    {
        iterations++;
        _ = Math.Sqrt(iterations);
    }

    return Results.Ok(new
    {
        scenario = "cpu-saturation",
        duration,
        iterations,
    });
});

lab.MapGet("/thread-pool", async (
    int? workers,
    int? delayMs,
    CancellationToken cancellationToken) =>
{
    var boundedWorkers = Math.Clamp(workers ?? 8, 1, 32);
    var boundedDelay = Math.Clamp(delayMs ?? 1000, 10, 5000);
    var jobs = Enumerable.Range(0, boundedWorkers)
        .Select(_ => Task.Run(() => Thread.Sleep(boundedDelay), cancellationToken));
    await Task.WhenAll(jobs);
    return Results.Ok(new
    {
        scenario = "thread-pool-starvation",
        workers = boundedWorkers,
        delayMs = boundedDelay,
    });
});

lab.MapPost("/memory", (int? megabytes, MemoryPressureState state) =>
{
    var boundedMegabytes = Math.Clamp(megabytes ?? 16, 1, 64);
    state.Retain(boundedMegabytes);
    return Results.Ok(new
    {
        scenario = "managed-memory-growth",
        retainedMegabytes = state.RetainedMegabytes,
    });
});

lab.MapDelete("/memory", (MemoryPressureState state) =>
{
    state.Release();
    return Results.Ok(new
    {
        scenario = "managed-memory-growth",
        retainedMegabytes = 0,
    });
});

lab.MapGet("/database", async (
    int? delayMs,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var boundedDelay = Math.Clamp(delayMs ?? 500, 1, 3000);
    await using var connection = new NpgsqlConnection(
        RequiredConnectionString(configuration));
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT pg_sleep(@delay_seconds)";
    command.Parameters.AddWithValue(
        "delay_seconds",
        boundedDelay / 1000d);
    await command.ExecuteNonQueryAsync(cancellationToken);
    return Results.Ok(new
    {
        scenario = "slow-database-command",
        delayMs = boundedDelay,
    });
});

lab.MapGet("/connection-pool", async (
    int? connections,
    int? holdMs,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var boundedConnections = Math.Clamp(connections ?? 4, 1, 10);
    var boundedHold = Math.Clamp(holdMs ?? 500, 10, 3000);
    var connectionString = RequiredConnectionString(configuration);
    var opened = new List<NpgsqlConnection>(boundedConnections);
    try
    {
        for (var index = 0; index < boundedConnections; index++)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            opened.Add(connection);
        }

        await Task.Delay(boundedHold, cancellationToken);
        return Results.Ok(new
        {
            scenario = "connection-pool-pressure",
            connections = opened.Count,
            holdMs = boundedHold,
        });
    }
    finally
    {
        foreach (var connection in opened)
        {
            await connection.DisposeAsync();
        }
    }
});

app.MapCampusDefaultEndpoints();
app.Run();

static string DelayBucket(int delayMs) => delayMs switch
{
    < 100 => "fast",
    < 500 => "moderate",
    _ => "slow",
};

static bool ConstantTimeEquals(string? expected, string supplied)
{
    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
    {
        return false;
    }

    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
    return expectedBytes.Length == suppliedBytes.Length &&
        CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
}

static string RequiredConnectionString(IConfiguration configuration) =>
    configuration.GetConnectionString("Troubleshooting")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Troubleshooting is required.");

public partial class Program;
