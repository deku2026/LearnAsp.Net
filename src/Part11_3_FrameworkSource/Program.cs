using System.Text;
using Campus.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Part11_3_FrameworkSource;

var builder = WebApplication.CreateBuilder(args);
builder.AddCampusServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<SimpleChangeTokenSource>();
builder.Services.Configure<LifecycleOptions>(builder.Configuration.GetSection("Lifecycle"));
builder.Services.AddSingleton<IAuthorizationHandler, LabPolicyHandler>();
builder.Services.AddAuthentication("Lab").AddCookie("Lab", o => { });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseExceptionHandler();
app.UseMiddleware<PipelineMiddleware>("outer");
app.UseMiddleware<PipelineMiddleware>("inner");
app.UseAuthentication();
app.UseAuthorization();

var lab = app.MapGroup("/lab")
    .AddEndpointFilter(async (context, next) =>
    {
        var httpContext = context.HttpContext;
        var cfg = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!cfg.GetValue("FrameworkSource:FaultInjectionEnabled", false))
        {
            return Results.NotFound();
        }
        var expected = cfg["FrameworkSource:LabToken"];
        var supplied = httpContext.Request.Headers["X-Lab-Token"].ToString();
        if (!ConstantTimeEquals(expected, supplied))
        {
            return Results.Unauthorized();
        }
        return await next(context);
    });

lab.MapGet("/di", (IServiceScopeFactory scopes) =>
{
    using var scope1 = scopes.CreateScope();
    using var scope2 = scopes.CreateScope();
    var id1 = scope1.GetHashCode();
    var id2 = scope2.GetHashCode();
    return Results.Ok(new ScopedIdDto(Guid.NewGuid(), Guid.NewGuid(), $"{id1}-{id2}"));
});

lab.MapGet("/pipeline", (HttpContext ctx) =>
{
    var before = ctx.Items.Keys
        .Where(k => k is string s && s.StartsWith("before:"))
        .Select(k => k.ToString()!)
        .ToList();
    var after = ctx.Items.Keys
        .Where(k => k is string s && s.StartsWith("after:"))
        .Select(k => k.ToString()!)
        .ToList();
    return Results.Ok(new PipelineTraceDto(before, after));
});

lab.MapGet("/endpoint-metadata", (HttpContext ctx) =>
{
    var endpoint = ctx.GetEndpoint();
    var policy = endpoint?.Metadata.GetMetadata<LabPolicy>();
    return Results.Ok(new MetadataReadDto(policy?.Name ?? "none", "LabPolicy", policy is not null));
}).WithMetadata(new LabPolicy("demo"));

lab.MapPost("/options", (SimpleChangeTokenSource source, IOptionsMonitor<LifecycleOptions> monitor) =>
{
    var changes = 0;
    // Register directly on the change token to demonstrate the IChangeToken
    // pattern that IOptionsMonitor.OnChange builds on top of.
    var token = source;
    var disposable = token.RegisterChangeCallback(_ => Interlocked.Increment(ref changes), null);
    try
    {
        source.Trigger();
        return Results.Ok(new OptionsChangeDto("SimpleChangeTokenSource", monitor.CurrentValue.DemoValue, changes));
    }
    finally
    {
        disposable.Dispose();
    }
});

lab.MapGet("/auth", (string? path) =>
{
    var p = path ?? "authenticate";
    return Results.Ok(new AuthPathDto(p, "Lab", p == "authenticate"));
});

lab.MapGet("/lifecycle", () =>
{
    var stages = new List<string>
    {
        "kestrel-received",
        "hostingapplication-context",
        "middleware-pipeline-fold",
        "routing-match-setendpoint",
        "authentication-user",
        "authorization-metadata-read",
        "endpoint-rdg-execute",
        "di-options-participate",
        "response-reverse",
        "kestrel-writeback",
    };
    return Results.Ok(new LifecycleTraceDto(stages));
});

app.MapCampusDefaultEndpoints();
app.Run();

static bool ConstantTimeEquals(string? expected, string supplied)
{
    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
    {
        return false;
    }
    var a = Encoding.UTF8.GetBytes(expected);
    var b = Encoding.UTF8.GetBytes(supplied);
    return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
}

public partial class Program;
