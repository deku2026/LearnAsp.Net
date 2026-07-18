// LearnAspNet
// Doc   : ASP.NetStudy/步骤8-日志-错误处理-健康检查-完整实施指南.md
// Part  : Step08 · LoggingErrorsHealth
// Title : 日志 · 错误处理 · 健康检查

using Campus.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Step08_LoggingErrorsHealth;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console();

        var seqUrl = ctx.Configuration["SEQ_URL"] ?? ctx.Configuration["Seq:ServerUrl"];
        if (!string.IsNullOrWhiteSpace(seqUrl))
        {
            cfg.WriteTo.Seq(seqUrl);
        }
    });

    builder.Services.AddSingleton<ICampusReadyGate, CampusReadyGate>();
    builder.Services.AddExceptionHandler<CampusExceptionHandler>();
    builder.Services.AddProblemDetails(o =>
    {
        o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Extensions["errorCode"] =
                ctx.HttpContext.Items["errorCode"] as string ?? ErrorCodes.InternalError;
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        };
    });

    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("process up"), tags: ["live"])
        .AddCheck<CampusReadinessHealthCheck>("campus-ready", tags: ["ready"]);

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.MapGet("/", () => Results.Ok(new { lab = "Step08_LoggingErrorsHealth" }));
    app.MapGet("/boom", (HttpContext _) => throw new InvalidOperationException("lab-boom"));
    app.MapPost("/ready-state", (ReadyStateBody body, ICampusReadyGate gate) =>
    {
        gate.IsReady = body.Ready;
        return Results.Ok(new { gate.IsReady });
    });

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("live"),
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("ready"),
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;

namespace Step08_LoggingErrorsHealth
{
    public sealed record ReadyStateBody(bool Ready);

    public interface ICampusReadyGate
    {
        bool IsReady { get; set; }
    }

    public sealed class CampusReadyGate : ICampusReadyGate
    {
        public bool IsReady { get; set; } = true;
    }

    public sealed class CampusReadinessHealthCheck(ICampusReadyGate gate) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(gate.IsReady
                ? HealthCheckResult.Healthy("ready")
                : HealthCheckResult.Unhealthy("dependencies not ready"));
        }
    }

    public sealed class CorrelationIdMiddleware(RequestDelegate next)
    {
        public const string HeaderName = "X-Correlation-ID";

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
                ? existing.ToString()
                : Guid.NewGuid().ToString("N");

            context.Response.Headers[HeaderName] = correlationId;
            using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
            {
                await next(context);
            }
        }
    }

    public sealed class CampusExceptionHandler(IProblemDetailsService problemDetails, IHostEnvironment env) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            httpContext.Items["errorCode"] = ErrorCodes.InternalError;
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

            await problemDetails.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An error occurred",
                    Detail = env.IsDevelopment() ? exception.Message : "Unexpected error",
                    Type = "https://httpstatuses.com/500",
                },
            });

            return true;
        }
    }
}
