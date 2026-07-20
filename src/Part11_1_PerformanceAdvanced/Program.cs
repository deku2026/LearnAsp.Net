using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using Campus.ServiceDefaults;
using Microsoft.AspNetCore.ResponseCompression;
using Part11_1_PerformanceAdvanced;

var builder = WebApplication.CreateBuilder(args);
builder.AddCampusServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<PerformanceState>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = false;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseResponseCompression();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part11_1_PerformanceAdvanced",
    topics = new[]
    {
        "threadpool starvation (blocking vs async)",
        "GC modes (workstation / server / DATAS)",
        "STJ source generation vs reflection",
        "Span/ArrayPool course-code parse",
        "response compression (brotli/gzip)",
        "MapStaticAssets fingerprinted report",
    },
    safety = "Fault endpoints under /lab/* are disabled by default and require X-Lab-Token.",
}));

app.MapGet("/api/performance/runtime", () => Results.Ok(new RuntimeInfoDto(
    IsServerGC: GCSettings.IsServerGC,
    ProcessorCount: Environment.ProcessorCount,
    Framework: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    ProcessArchitecture: System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    GcMode: GCSettings.IsServerGC ? "Server" : "Workstation",
    DynamicAdaptation: IsDynamicAdaptationEnabled())));

app.MapPost("/api/performance/course-codes/parse", (ParseRequest request, string? impl) =>
{
    var useSpan = string.Equals(impl, "span", StringComparison.OrdinalIgnoreCase);
    var result = useSpan
        ? CourseCodeParser.ParseSpan(request.Code)
        : CourseCodeParser.ParseBaseline(request.Code);
    return result is null
        ? Results.Problem(statusCode: 400, title: "Invalid course code", detail: request.Code)
        : Results.Ok(result);
});

app.MapPost("/api/performance/serialize", (SerializeRequest request, string? impl) =>
{
    var useSourceGen = string.Equals(impl, "sourcegen", StringComparison.OrdinalIgnoreCase);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    byte[] bytes;
    if (useSourceGen)
    {
        bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            request.Summary, PerformanceJsonContext.Default.EnrollmentSummaryDto);
    }
    else
    {
        bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request.Summary);
    }
    sw.Stop();
    return Results.Ok(new SerializeResultDto(bytes.Length, sw.ElapsedTicks));
});

app.MapGet("/api/performance/payload", (int? bytes) =>
{
    var bounded = Math.Clamp(bytes ?? 2048, 16, 1_000_000);
    var text = new string('A', bounded);
    return Results.Ok(new PayloadDto(text, bounded));
});

var lab = app.MapGroup("/lab")
    .AddEndpointFilter(async (context, next) =>
    {
        var httpContext = context.HttpContext;
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("Performance:FaultInjectionEnabled", false))
        {
            return Results.NotFound();
        }
        var expected = configuration["Performance:LabToken"];
        var supplied = httpContext.Request.Headers["X-Lab-Token"].ToString();
        if (!ConstantTimeEquals(expected, supplied))
        {
            return Results.Unauthorized();
        }
        return await next(context);
    });

lab.MapGet("/threadpool/blocking", (int? delayMs) =>
{
    var boundedDelay = Math.Clamp(delayMs ?? 200, 10, 2000);
    Thread.Sleep(boundedDelay);
    return Results.Ok(new { scenario = "threadpool-blocking", delayMs = boundedDelay });
});

lab.MapGet("/threadpool/async", async (int? delayMs, CancellationToken ct) =>
{
    var boundedDelay = Math.Clamp(delayMs ?? 200, 10, 2000);
    await Task.Delay(boundedDelay, ct);
    return Results.Ok(new { scenario = "threadpool-async", delayMs = boundedDelay });
});

lab.MapPost("/gc/allocate", (int? megabytes, PerformanceState state) =>
{
    var bounded = Math.Clamp(megabytes ?? 16, 1, 64);
    state.Retain(bounded);
    return Results.Ok(new { scenario = "gc-allocate", retainedMegabytes = state.RetainedMegabytes });
});

lab.MapDelete("/gc/allocate", (PerformanceState state) =>
{
    state.Release();
    return Results.Ok(new { scenario = "gc-allocate", retainedMegabytes = 0 });
});

app.MapCampusDefaultEndpoints();
app.Run();

static bool IsDynamicAdaptationEnabled()
{
    var v = Environment.GetEnvironmentVariable("DOTNET_GCDynamicAdaptationMode");
    return v is null || v == "1";
}

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

public partial class Program;
