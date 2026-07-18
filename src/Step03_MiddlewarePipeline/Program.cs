// LearnAspNet
// Doc   : ASP.NetStudy/步骤3-中间件管道-完整实施指南.md
// Part  : Step03 · MiddlewarePipeline
// Title : 中间件管道

using System.Diagnostics;
using System.Text.Json;
using Campus.Contracts;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Order: exception outer → timing → endpoints
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

app.MapGet("/", () => Results.Ok(new { lab = "Step03_MiddlewarePipeline" }));
app.MapGet("/ok", () => Results.Ok(new { message = "ok" }));
app.MapGet("/boom", (HttpContext _) => throw new InvalidOperationException("boom-for-lab"));

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
