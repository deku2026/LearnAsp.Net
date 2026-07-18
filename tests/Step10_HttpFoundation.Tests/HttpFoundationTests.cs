using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Step10_HttpFoundation.Tests;

public sealed class HttpFoundationTests : IAsyncLifetime
{
    private WireMockServer _wireMock = null!;

    public Task InitializeAsync()
    {
        _wireMock = WireMockServer.Start();
        _wireMock
            .Given(Request.Create().WithPath("/catalog/CS101").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"code":"CS101","title":"Intro","provider":"wiremock"}"""));

        _wireMock
            .Given(Request.Create().WithPath("/catalog/MISSING").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _wireMock.Stop();
        _wireMock.Dispose();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var baseUrl = _wireMock.Url!.TrimEnd('/') + "/";
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ExternalCatalog:BaseUrl", baseUrl);
        });
    }

    [Fact]
    public async Task Kestrel_limits_endpoint_reports_configured_values()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>("/kestrel-limits");
        Assert.Equal(256, json.GetProperty("maxConcurrentConnections").GetInt32());
        Assert.Equal(65536, json.GetProperty("maxRequestBodyBytes").GetInt64());
    }

    [Fact]
    public async Task Proxy_catalog_returns_external_payload()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/proxy/catalog/CS101");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("CS101", json.GetProperty("code").GetString());
        Assert.Equal("wiremock", json.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Proxy_catalog_not_found()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/proxy/catalog/MISSING");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Client_info_endpoint_ok()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/client-info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
