using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Step10_HttpFoundation;

var builder = WebApplication.CreateBuilder(args);

const long MaxRequestBodyBytes = 1024;
const int MaxConcurrentConnections = 256;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = MaxConcurrentConnections;
    options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Security: only trust known proxies — do NOT clear these lists in production.
    // Clearing trusts arbitrary client X-Forwarded-For claims (spoofing hole).
    o.KnownProxies.Add(IPAddress.Loopback);
    o.KnownProxies.Add(IPAddress.IPv6Loopback);
    // Add configured proxies from "ForwardedHeaders:KnownProxies" (comma-separated) if present.
    var configured = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
    if (configured is not null)
    {
        foreach (var ip in configured)
        {
            if (IPAddress.TryParse(ip, out var addr))
            {
                o.KnownProxies.Add(addr);
            }
        }
    }
});

builder.Services.AddHttpContextAccessor();

var catalogBase = builder.Configuration["ExternalCatalog:BaseUrl"] ?? "http://127.0.0.1:9/";
builder.Services.AddTransient<CorrelationIdHandler>();
builder.Services.AddTransient<CatalogAuthHeaderHandler>();
builder.Services.AddHttpClient<IExternalCatalogClient, ExternalCatalogClient>(client =>
    {
        client.BaseAddress = new Uri(catalogBase);
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<CatalogAuthHeaderHandler>()
    .AddStandardResilienceHandler(pipeline =>
    {
        pipeline.Retry.MaxRetryAttempts = 2;
        pipeline.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(
            builder.Configuration.GetValue("Resilience:AttemptTimeoutMs", 3000));
        pipeline.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(
            builder.Configuration.GetValue("Resilience:TotalTimeoutMs", 10000));
        pipeline.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(
            builder.Configuration.GetValue("Resilience:CircuitSamplingMs", 30000));
        pipeline.CircuitBreaker.MinimumThroughput =
            builder.Configuration.GetValue("Resilience:CircuitMinimumThroughput", 100);
        pipeline.CircuitBreaker.BreakDuration = TimeSpan.FromMilliseconds(
            builder.Configuration.GetValue("Resilience:CircuitBreakMs", 5000));
    });

var app = builder.Build();

app.UseForwardedHeaders();

app.MapGet("/", () => Results.Ok(new { lab = "Step10_HttpFoundation" }));

app.MapGet("/kestrel-limits", () => Results.Ok(new
{
    maxConcurrentConnections = MaxConcurrentConnections,
    maxRequestBodyBytes = MaxRequestBodyBytes,
    keepAliveTimeoutSeconds = 30,
    requestHeadersTimeoutSeconds = 15,
}));

app.MapGet("/http-version", (HttpContext ctx) => Results.Ok(new { protocol = ctx.Request.Protocol }));

app.MapGet("/remote-ip", (HttpContext ctx) => Results.Ok(new
{
    remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    scheme = ctx.Request.Scheme,
    forwardedFor = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? "",
}));

app.MapPost("/upload", async (HttpRequest request, CancellationToken ct) =>
{
    await request.Body.CopyToAsync(Stream.Null, ct);
    return Results.Ok(new { accepted = request.ContentLength ?? 0 });
});

app.MapPost("/upload/large", async (HttpRequest request, CancellationToken ct) =>
{
    await request.Body.CopyToAsync(Stream.Null, ct);
    return Results.Ok(new { accepted = request.ContentLength ?? 0 });
}).WithMetadata(new RequestSizeLimitAttribute(128 * 1024));

app.MapGet("/proxy/catalog/{code}", async (string code, IExternalCatalogClient catalog, CancellationToken ct) =>
{
    var item = await catalog.GetByCodeAsync(code, ct);
    return item is null
        ? Results.NotFound(new { errorCode = "catalog.not_found", code })
        : Results.Ok(item);
});

app.MapGet("/client-info", (IExternalCatalogClient _) =>
    Results.Ok(new
    {
        message = "Use typed IHttpClientFactory clients + AddStandardResilienceHandler; never new HttpClient() per request.",
        client = nameof(ExternalCatalogClient),
    }));

app.Run();

public partial class Program;

/// <summary>Outbound DelegatingHandler: propagates X-Correlation-ID from incoming request to outbound HttpClient calls.</summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ctx = accessor.HttpContext;
        if (ctx is not null &&
            ctx.Request.Headers.TryGetValue("X-Correlation-ID", out var inbound) &&
            !string.IsNullOrWhiteSpace(inbound))
        {
            var correlationId = inbound.ToString();
            if (correlationId.Length <= 128 && correlationId.All(ch => !char.IsControl(ch)))
            {
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Adds the external catalog credential in one outbound handler instead of at call sites.</summary>
public sealed class CatalogAuthHeaderHandler(IConfiguration configuration) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["ExternalCatalog:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Catalog-Key", apiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
