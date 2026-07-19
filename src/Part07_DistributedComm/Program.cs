using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Part06_2_MessagingTools;
using Part07_DistributedComm;
using Part07_DistributedComm.Grpc;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
var role = builder.Configuration["Distributed:Role"]?.Trim() ?? "Gateway";

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

switch (role.ToLowerInvariant())
{
    case "gateway":
        ConfigureGateway(builder);
        break;
    case "catalog":
        builder.Services.AddSingleton<CatalogStore>();
        builder.Services.AddGrpc();
        builder.Services.AddHealthChecks()
            .AddCheck<CatalogDatabaseHealthCheck>("postgres", tags: ["ready"]);
        break;
    case "enrollment":
        ConfigureEnrollment(builder);
        break;
    case "notices":
        ConfigureNotices(builder);
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown Distributed:Role '{role}'. Use Gateway, Catalog, Enrollment, or Notices.");
}

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = error switch
    {
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } =>
            (StatusCodes.Status409Conflict, "Duplicate enrollment"),
        RpcException { StatusCode: StatusCode.Unavailable } =>
            (StatusCodes.Status503ServiceUnavailable, "Catalog gRPC unavailable"),
        _ => (StatusCodes.Status503ServiceUnavailable, "Distributed dependency failure"),
    };
    await Results.Problem(
        statusCode: status,
        title: title,
        detail: app.Environment.IsDevelopment() ||
                app.Environment.IsEnvironment("Testing")
            ? error?.Message
            : null)
        .ExecuteAsync(context);
}));

app.Use(async (context, next) =>
{
    var inbound = context.Request.Headers["X-Correlation-ID"].ToString();
    var correlationId = IsSafeCorrelationId(inbound)
        ? inbound
        : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    context.Request.Headers["X-Correlation-ID"] = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    await next();
});

if (string.Equals(role, "Gateway", StringComparison.OrdinalIgnoreCase))
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var subject = context.User.FindFirstValue("sub") ??
                          context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                          "authenticated-user";
            context.Request.Headers["X-Campus-User"] = subject;
            context.Request.Headers["X-Campus-Gateway"] =
                app.Configuration["GatewayAuth:InternalToken"];
        }
        else
        {
            context.Request.Headers.Remove("X-Campus-User");
            context.Request.Headers.Remove("X-Campus-Gateway");
        }

        await next();
    });
}

await InitializeRoleAsync(app.Services, role);

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part07_DistributedComm",
    role,
    architecture = "REST outside, gRPC inside, RabbitMQ for asynchronous integration events",
    serviceDiscovery = "URLs come from configuration; production maps the same logical names via Aspire or Kubernetes DNS.",
    deployment = "One independently configurable process per bounded context.",
}));

switch (role.ToLowerInvariant())
{
    case "gateway":
        app.MapReverseProxy().RequireAuthorization("gateway");
        break;
    case "catalog":
        MapCatalog(app);
        break;
    case "enrollment":
        MapEnrollment(app);
        break;
    case "notices":
        MapNotices(app);
        break;
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();

static void ConfigureGateway(WebApplicationBuilder builder)
{
    var issuer = builder.Configuration["GatewayAuth:Issuer"] ?? "campus-gateway";
    var audience = builder.Configuration["GatewayAuth:Audience"] ?? "campus-capstone";
    var signingKey = builder.Configuration["GatewayAuth:SigningKey"]
        ?? throw new InvalidOperationException("GatewayAuth:SigningKey is required.");
    var internalToken = builder.Configuration["GatewayAuth:InternalToken"]
        ?? throw new InvalidOperationException("GatewayAuth:InternalToken is required.");
    if (Encoding.UTF8.GetByteCount(signingKey) < 32)
    {
        throw new InvalidOperationException(
            "GatewayAuth:SigningKey must contain at least 32 UTF-8 bytes.");
    }
    if (Encoding.UTF8.GetByteCount(internalToken) < 32)
    {
        throw new InvalidOperationException(
            "GatewayAuth:InternalToken must contain at least 32 UTF-8 bytes.");
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(signingKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        });
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy("gateway", policy => policy.RequireAuthenticatedUser());

    var routes = new[]
    {
        GatewayRoute("catalog", "/api/catalog/{**catch-all}", "/api/catalog"),
        GatewayRoute("enrollment", "/api/enrollments/{**catch-all}", "/api"),
        GatewayRoute("notices", "/api/notices/{**catch-all}", "/api/notices"),
    };
    var clusters = new[]
    {
        GatewayCluster(
            "catalog",
            builder.Configuration["Distributed:CatalogHttpUrl"] ??
            "http://127.0.0.1:6021/"),
        GatewayCluster(
            "enrollment",
            builder.Configuration["Distributed:EnrollmentUrl"] ??
            "http://127.0.0.1:6022/"),
        GatewayCluster(
            "notices",
            builder.Configuration["Distributed:NoticesUrl"] ??
            "http://127.0.0.1:6023/"),
    };
    builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters);
    builder.Services.AddHttpClient("gateway-health", client =>
        client.Timeout = TimeSpan.FromSeconds(2))
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseProxy = false,
        });
    builder.Services.AddHealthChecks()
        .AddCheck<GatewayDependenciesHealthCheck>("destinations", tags: ["ready"]);
}

static void ConfigureEnrollment(WebApplicationBuilder builder)
{
    builder.Services.AddSingleton<EnrollmentStore>();
    var catalogGrpcUrl = builder.Configuration["Distributed:CatalogGrpcUrl"]
        ?? "http://127.0.0.1:6121/";
    builder.Services.AddGrpcClient<Catalog.CatalogClient>(options =>
        options.Address = new Uri(catalogGrpcUrl))
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseProxy = false,
        })
        .AddStandardResilienceHandler(options =>
            ConfigureResilience(options, builder.Configuration));
    builder.Services.AddSingleton<CatalogGrpcClient>();

    var noticesUrl = builder.Configuration["Distributed:NoticesUrl"]
        ?? "http://127.0.0.1:6023/";
    builder.Services.AddHttpClient<NoticesClient>(client =>
        {
            client.BaseAddress = new Uri(noticesUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseProxy = false,
        })
        .AddStandardResilienceHandler(options =>
            ConfigureResilience(options, builder.Configuration));

    builder.Services.AddSingleton<RabbitMqConnection>();
    builder.Services.AddSingleton<IHostedService>(services =>
        services.GetRequiredService<RabbitMqConnection>());
    builder.Services.AddHostedService<DistributedOutboxRelay>();
    builder.Services.AddHealthChecks()
        .AddCheck<EnrollmentDatabaseHealthCheck>("postgres", tags: ["ready"])
        .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
}

static void ConfigureNotices(WebApplicationBuilder builder)
{
    builder.Services.AddSingleton<RabbitInboxStore>();
    builder.Services.AddSingleton<RabbitMqConnection>();
    builder.Services.AddSingleton<IHostedService>(services =>
        services.GetRequiredService<RabbitMqConnection>());
    builder.Services.AddHostedService<RabbitMqConsumer>();
    builder.Services.AddSingleton<FaultInjectionState>();
    builder.Services.AddHealthChecks()
        .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"])
        .AddCheck<RabbitStoreHealthCheck>("postgres", tags: ["ready"]);
}

static void ConfigureResilience(
    HttpStandardResilienceOptions options,
    IConfiguration configuration)
{
    options.Retry.MaxRetryAttempts =
        configuration.GetValue("Resilience:RetryAttempts", 1);
    options.Retry.Delay = TimeSpan.FromMilliseconds(
        configuration.GetValue("Resilience:RetryDelayMs", 50));
    options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(
        configuration.GetValue("Resilience:AttemptTimeoutMs", 500));
    options.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(
        configuration.GetValue("Resilience:TotalTimeoutMs", 2000));
    options.CircuitBreaker.MinimumThroughput =
        configuration.GetValue("Resilience:CircuitMinimumThroughput", 2);
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(
        configuration.GetValue("Resilience:CircuitSamplingMs", 4000));
    options.CircuitBreaker.BreakDuration = TimeSpan.FromMilliseconds(
        configuration.GetValue("Resilience:CircuitBreakMs", 1000));
}

static RouteConfig GatewayRoute(
    string id,
    string path,
    string prefix) => new()
    {
        RouteId = $"{id}-route",
        ClusterId = $"{id}-cluster",
        Match = new RouteMatch { Path = path },
        Transforms =
    [
        new Dictionary<string, string>
        {
            ["PathRemovePrefix"] = prefix,
        },
    ],
    };

static ClusterConfig GatewayCluster(string id, string address) => new()
{
    ClusterId = $"{id}-cluster",
    LoadBalancingPolicy = "RoundRobin",
    Destinations = new Dictionary<string, DestinationConfig>
    {
        [$"{id}-primary"] = new() { Address = address },
    },
};

static async Task InitializeRoleAsync(IServiceProvider services, string role)
{
    switch (role.ToLowerInvariant())
    {
        case "catalog":
            await services.GetRequiredService<CatalogStore>()
                .InitializeAsync(CancellationToken.None);
            break;
        case "enrollment":
            await services.GetRequiredService<EnrollmentStore>()
                .InitializeAsync(CancellationToken.None);
            break;
        case "notices":
            await services.GetRequiredService<RabbitInboxStore>()
                .InitializeAsync(CancellationToken.None);
            break;
    }
}

static void MapCatalog(WebApplication app)
{
    app.MapGrpcService<CatalogGrpcService>();
    app.MapGet("/courses/{id:guid}", async (
        Guid id,
        CatalogStore store,
        CancellationToken cancellationToken) =>
    {
        var course = await store.GetAsync(id, cancellationToken);
        return course is null ? Results.NotFound() : Results.Ok(course);
    }).AddEndpointFilter<GatewayHeaderFilter>();
}

static void MapEnrollment(WebApplication app)
{
    app.MapPost("/enrollments", async (
        CreateDistributedEnrollment request,
        HttpContext context,
        CatalogGrpcClient catalog,
        EnrollmentStore store,
        CancellationToken cancellationToken) =>
    {
        var course = await catalog.GetAsync(request.CourseId, cancellationToken);
        if (course is null)
        {
            return Results.NotFound(new
            {
                errorCode = "catalog.course_not_found",
                request.CourseId,
            });
        }

        var enrollment = await store.CreateAsync(
            request.StudentId,
            course,
            context.Request.Headers["X-Campus-User"].ToString(),
            context.Request.Headers["X-Correlation-ID"].ToString(),
            cancellationToken);
        return Results.Accepted($"/enrollments/{enrollment.Id}", enrollment);
    }).AddEndpointFilter<GatewayHeaderFilter>();

    app.MapGet("/enrollments", async (
        EnrollmentStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(cancellationToken)))
        .AddEndpointFilter<GatewayHeaderFilter>();

    app.MapGet("/enrollments/resilience/notices", async (
        NoticesClient notices,
        CancellationToken cancellationToken) =>
        Results.Ok(await notices.ProbeAsync(cancellationToken)))
        .AddEndpointFilter<GatewayHeaderFilter>();
}

static void MapNotices(WebApplication app)
{
    app.MapGet("/notifications", async (
        RabbitInboxStore store,
        CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(cancellationToken)))
        .AddEndpointFilter<GatewayHeaderFilter>();

    app.MapPost("/internal/fault/configure/{failures:int}", (
        int failures,
        FaultInjectionState state) =>
    {
        state.Configure(failures);
        return Results.NoContent();
    });
    app.MapGet("/internal/fault/probe", (FaultInjectionState state) =>
    {
        return state.ShouldFail()
            ? Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Injected notices failure")
            : Results.Ok(new NoticesProbeResult(state.RequestCount, "healthy"));
    });
    app.MapGet("/internal/fault/stats", (FaultInjectionState state) =>
        Results.Ok(new NoticesProbeResult(state.RequestCount, "observed")));
    app.MapPost("/internal/rabbit/purge", async (
        RabbitMqConnection rabbit,
        RabbitInboxStore store,
        CancellationToken cancellationToken) =>
    {
        await rabbit.PurgeAsync(cancellationToken);
        await store.ResetAsync(cancellationToken);
        return Results.NoContent();
    });
}

static bool IsSafeCorrelationId(string value) =>
    !string.IsNullOrWhiteSpace(value) &&
    value.Length <= 128 &&
    value.All(character => !char.IsControl(character));

public partial class Program;

public sealed record CreateDistributedEnrollment(Guid StudentId, Guid CourseId);
