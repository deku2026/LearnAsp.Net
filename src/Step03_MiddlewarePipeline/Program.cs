// LearnAspNet
// Doc   : ASP.NetStudy/步骤3-中间件管道-完整实施指南.md
// Part  : Step03 · MiddlewarePipeline
// Title : 中间件管道

using System.Diagnostics;
using System.Text.Json;
using Campus.Contracts;

var builder = WebApplication.CreateBuilder(args);

// IMiddleware factory style: per-request DI activation (can inject scoped services).
builder.Services.AddScoped<RequestContext>();
// Factory-activated middleware is registered via AddMiddleware<T> on IServiceCollection.
builder.Services.AddTransient<FactoryMiddleware>();

// Auth-order experiment services (only used when Lab:AuthOrderExperiment=true).
builder.Services.AddAuthentication("Fake")
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, FakeAuthHandler>("Fake", _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

// ShortCircuit: 404 for favicon without running auth (must be early).
app.MapShortCircuit(StatusCodes.Status404NotFound, "/favicon.ico");

// Auth-order experiment: when flag is set, deliberately put UseAuthorization BEFORE UseAuthentication.
if (app.Configuration["Lab:AuthOrderExperiment"] == "true")
{
    // WRONG order: authz before authn → [Authorize] endpoints return 401 even with valid token.
    app.UseAuthorization();
    app.UseAuthentication();
}
else
{
    // Correct order: authn before authz.
    app.UseAuthentication();
    app.UseAuthorization();
}

// Exception middleware (outermost)
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ExceptionMiddleware");
        logger.LogError(ex, "Unhandled exception");

        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "https://httpstatuses.com/500",
            title = "An error occurred",
            status = 500,
            detail = app.Environment.IsDevelopment() ? ex.Message : "Unexpected error",
            errorCode = ErrorCodes.InternalError,
            traceId = context.TraceIdentifier,
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
});

// Timing middleware
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Elapsed-ms"] = sw.ElapsedMilliseconds.ToString();
        return Task.CompletedTask;
    });
    await next();
});

// IMiddleware factory style: shows per-request scoped DI activation.
app.UseMiddleware<FactoryMiddleware>();

// UseWhen: branch that REJOINS the main pipeline (unlike Map which terminates).
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/diag"),
    bran => bran.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Diag-Branch"] = "visited";
        await next(); // continues to downstream middleware AND rejoins main pipeline
    }));

// MapWhen: terminal branch (does NOT rejoin).
app.MapWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/terminal-diag"),
    bran => bran.Run(async ctx =>
    {
        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsync("terminal-diag-branch");
    }));

app.MapGet("/", () => Results.Ok(new { lab = "Step03_MiddlewarePipeline" }));
app.MapGet("/ok", () => Results.Ok(new { message = "ok" }));
app.MapGet("/boom", (HttpContext _) => throw new InvalidOperationException("boom-for-lab"));
app.MapGet("/diag", () => Results.Ok(new { diag = true }));
app.MapGet("/whoami", (HttpContext ctx) => Results.Ok(new
{
    user = ctx.User.Identity?.Name ?? "anonymous",
    isAuthenticated = ctx.User.Identity?.IsAuthenticated ?? false,
})).RequireAuthorization();

app.Map("/branch", branch =>
{
    branch.Run(async ctx =>
    {
        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsync("branch-terminal");
    });
});

app.Run();

public partial class Program;

/// <summary>Scoped service resolved per-request via IMiddleware factory style.</summary>
public sealed class RequestContext
{
    public Guid RequestId { get; } = Guid.NewGuid();
}

/// <summary>IMiddleware (factory style): ctor injection with scoped services, per-request activation.</summary>
public sealed class FactoryMiddleware(RequestContext scopedContext, ILogger<FactoryMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        httpContext.Response.Headers["X-Factory-MW-Request-Id"] = scopedContext.RequestId.ToString();
        logger.LogDebug("FactoryMiddleware activated for request {RequestId}", scopedContext.RequestId);
        await next(httpContext);
    }
}

/// <summary>Simple auth handler for the auth-order experiment (always "authenticates" with a fixed user).</summary>
public sealed class FakeAuthHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "fake-user"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "User"),
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Fake");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(
            new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, "Fake")));
    }
}