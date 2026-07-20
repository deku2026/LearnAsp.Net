using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Part11_3_FrameworkSource.Tests;

public sealed class FrameworkSourceTests : IClassFixture<FrameworkSourceFactory>
{
    private readonly FrameworkSourceFactory _factory;

    public FrameworkSourceTests(FrameworkSourceFactory factory) => _factory = factory;

    [Fact]
    public async Task FaultLabIsNotDiscoverableWhenDisabled()
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(
            (_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FrameworkSource:FaultInjectionEnabled"] = "false",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
            })));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/lab/di");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FaultLabRejectsMissingToken()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/lab/di");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DiEndpointReturnsScopeIds()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lab-Token", "test-lab-token");
        var result = await client.GetFromJsonAsync<ScopedId>("/lab/di");
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.RequestId);
        Assert.NotEqual(Guid.Empty, result.ScopeId);
        Assert.False(string.IsNullOrEmpty(result.ScopeHash));
    }

    [Fact]
    public async Task PipelineEndpointReportsBeforeMarkers()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lab-Token", "test-lab-token");
        var trace = await client.GetFromJsonAsync<PipelineTrace>("/lab/pipeline");
        Assert.NotNull(trace);
        Assert.NotEmpty(trace!.Before);
        // The "after" markers are written by the middleware after the endpoint
        // returns, so they are not visible inside the endpoint. The before
        // markers prove the reverse-fold pipeline executed.
        Assert.Contains(trace.Before, b => b.StartsWith("before:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EndpointMetadataReadsLabPolicyFromEndpoint()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lab-Token", "test-lab-token");
        var meta = await client.GetFromJsonAsync<MetadataRead>("/lab/endpoint-metadata");
        Assert.NotNull(meta);
        Assert.Equal("demo", meta!.Policy);
        Assert.True(meta.Present);
    }

    [Fact]
    public async Task OptionsEndpointReportsChangeAfterTrigger()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lab-Token", "test-lab-token");
        using var response = await client.PostAsync("/lab/options", null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"changes\"", body, StringComparison.OrdinalIgnoreCase);
        var result = await response.Content.ReadFromJsonAsync<OptionsChange>();
        Assert.NotNull(result);
        Assert.True(result!.Changes > 0, $"Expected changes > 0 but got {result.Changes}. Body: {body}");
    }

    [Fact]
    public async Task AuthEndpointReportsRequestedPath()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lab-Token", "test-lab-token");
        var result = await client.GetFromJsonAsync<AuthPath>("/lab/auth?path=challenge");
        Assert.NotNull(result);
        Assert.Equal("challenge", result!.Path);
        Assert.Equal("Lab", result.Scheme);
    }

    [Fact]
    public async Task LifecycleEndpointReturnsTenStages()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lab-Token", "test-lab-token");
        var trace = await client.GetFromJsonAsync<LifecycleTrace>("/lab/lifecycle");
        Assert.NotNull(trace);
        Assert.Equal(10, trace!.Stages.Count);
        Assert.Equal("kestrel-received", trace.Stages[0]);
        Assert.Equal("kestrel-writeback", trace.Stages[9]);
    }

    private sealed record ScopedId(
        [property: JsonPropertyName("requestId")] Guid RequestId,
        [property: JsonPropertyName("scopeId")] Guid ScopeId,
        [property: JsonPropertyName("scopeHash")] string ScopeHash);

    private sealed record PipelineTrace(
        [property: JsonPropertyName("before")] IReadOnlyList<string> Before,
        [property: JsonPropertyName("after")] IReadOnlyList<string> After);

    private sealed record MetadataRead(
        [property: JsonPropertyName("policy")] string Policy,
        [property: JsonPropertyName("requirement")] string Requirement,
        [property: JsonPropertyName("present")] bool Present);

    private sealed record OptionsChange(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("changes")] int Changes);

    private sealed record AuthPath(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("scheme")] string Scheme,
        [property: JsonPropertyName("authenticated")] bool Authenticated);

    private sealed record LifecycleTrace(
        [property: JsonPropertyName("stages")] IReadOnlyList<string> Stages);
}

public sealed class FrameworkSourceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["FrameworkSource:FaultInjectionEnabled"] = "true",
                ["FrameworkSource:LabToken"] = "test-lab-token",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
            }));
    }
}
