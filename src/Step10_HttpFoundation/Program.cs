// LearnAspNet
// Doc   : ASP.NetStudy/步骤10-HTTP底座-Kestrel-HttpClientFactory-完整实施指南.md
// Part  : Step10 · HttpFoundation
// Title : HTTP 底座 · Kestrel · HttpClientFactory

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
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var catalogBase = builder.Configuration["ExternalCatalog:BaseUrl"] ?? "http://127.0.0.1:9/";
builder.Services.AddHttpClient<IExternalCatalogClient, ExternalCatalogClient>(client =>
    {
        client.BaseAddress = new Uri(catalogBase);
        client.Timeout = TimeSpan.FromSeconds(10);
    })
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
