using Campus.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
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
    app.MapGet("/boom", (ILogger<Program> logger) =>
    {
        LoggerMessages.LogBoom(logger);
        throw new InvalidOperationException("lab-boom");
    });
    app.MapPost("/ready-state", (ReadyStateBody body, ICampusReadyGate gate) =>
    {
        gate.IsReady = body.Ready;
        return Results.Ok(new { gate.IsReady });
    });

    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("live"),
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains("ready"),
        ResponseWriter = HealthResponseWriter.WriteAsync,
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
        private int _isReady = 1;

        public bool IsReady
        {
            get => Volatile.Read(ref _isReady) == 1;
            set => Volatile.Write(ref _isReady, value ? 1 : 0);
        }
    }

    public sealed class CampusReadinessHealthCheck(
        ICampusReadyGate gate,
        IConfiguration configuration) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var connectionString = configuration.GetConnectionString("Postgres");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return gate.IsReady
                    ? HealthCheckResult.Healthy("ready (in-memory lab gate)")
                    : HealthCheckResult.Unhealthy("dependencies not ready");
            }

            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                _ = await command.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy("postgres ready");
            }
            catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
            {
                return HealthCheckResult.Unhealthy("postgres unavailable", exception);
            }
        }
    }

    public sealed class CorrelationIdMiddleware(RequestDelegate next)
    {
        public const string HeaderName = "X-Correlation-ID";

        public async Task InvokeAsync(HttpContext context)
        {
            var supplied = context.Request.Headers.TryGetValue(HeaderName, out var existing)
                ? existing.ToString()
                : null;
            var correlationId = IsValidCorrelationId(supplied)
                ? supplied!
                : Guid.NewGuid().ToString("N");

            context.Response.Headers[HeaderName] = correlationId;
            using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
            {
                await next(context);
            }
        }

        private static bool IsValidCorrelationId(string? value) =>
            value is { Length: >= 1 and <= 64 } &&
            value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.');
    }

    public sealed class CampusExceptionHandler(
        IProblemDetailsService problemDetails,
        IHostEnvironment env,
        ILogger<CampusExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var (status, errorCode, title) = exception switch
            {
                KeyNotFoundException => (
                    StatusCodes.Status404NotFound,
                    ErrorCodes.NotFound,
                    "Resource not found"),
                ArgumentException => (
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.ValidationFailed,
                    "Invalid request"),
                _ => (
                    StatusCodes.Status500InternalServerError,
                    ErrorCodes.InternalError,
                    "An error occurred"),
            };

            LoggerMessages.LogUnhandled(logger, httpContext.Request.Path, exception);
            httpContext.Items["errorCode"] = errorCode;
            httpContext.Response.StatusCode = status;

            await problemDetails.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = status,
                    Title = title,
                    Detail = env.IsDevelopment() ? exception.Message : null,
                    Type = $"https://httpstatuses.com/{status}",
                },
            });

            return true;
        }
    }

    // [LoggerMessage] source generator: zero-allocation structured logging for hot paths.
    public static partial class LoggerMessages
    {
        [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Unhandled exception at {Path}")]
        public static partial void LogUnhandled(
            Microsoft.Extensions.Logging.ILogger logger,
            string path,
            Exception exception);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Boom endpoint hit")]
        public static partial void LogBoom(Microsoft.Extensions.Logging.ILogger logger);
    }

    public static class HealthResponseWriter
    {
        public static Task WriteAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(new
            {
                status = report.Status.ToString(),
                durationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries
                    .OrderBy(entry => entry.Key)
                    .Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        durationMs = entry.Value.Duration.TotalMilliseconds,
                    }),
            });
        }
    }
}
