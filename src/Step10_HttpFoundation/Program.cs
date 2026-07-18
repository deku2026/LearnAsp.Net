using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Step10_HttpFoundation;

var builder = WebApplication.CreateBuilder(args);

const long MaxRequestBodyBytes = 64 * 1024;
const int MaxConcurrentConnections = 256;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = MaxConcurrentConnections;
    options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    // Explicit HTTP/1.1+HTTP/2 awareness (HTTP/3 needs QUIC + alpn, out of scope for lab).
    options.ListenLocalhost(5010, lo => lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2);
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
builder.Services.AddHttpClient<IExternalCatalogClient, ExternalCatalogClient>(client =>
    {
        client.BaseAddress = new Uri(catalogBase);
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddStandardResilienceHandler(pipeline =>
    {
        pipeline.Retry.MaxRetryAttempts = 2;
        pipeline.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        pipeline.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
        pipeline.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
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
    forwardedFor = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? "",
}));

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
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", inbound.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
