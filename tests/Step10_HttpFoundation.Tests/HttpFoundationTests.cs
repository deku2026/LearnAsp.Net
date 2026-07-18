using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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

    private async Task WithFactoryAsync(Func<HttpClient, Task> test)
    {
        var baseUrl = _wireMock.Url!.TrimEnd('/') + "/";
        await using var factory = new Step10WebApplicationFactory(baseUrl);
        var client = factory.CreateClient();
        await test(client);
    }

    [Fact]
    public Task Kestrel_limits_endpoint_reports_configured_values()
        => WithFactoryAsync(async client =>
        {
            var json = await client.GetFromJsonAsync<JsonElement>("/kestrel-limits");
            Assert.Equal(256, json.GetProperty("maxConcurrentConnections").GetInt32());
            Assert.Equal(65536, json.GetProperty("maxRequestBodyBytes").GetInt64());
        });

    [Fact]
    public Task Proxy_catalog_returns_external_payload()
        => WithFactoryAsync(async client =>
        {
            var response = await client.GetAsync("/proxy/catalog/CS101");
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var json = JsonDocument.Parse(body).RootElement;
            Assert.Equal("CS101", json.GetProperty("code").GetString());
            Assert.Equal("wiremock", json.GetProperty("provider").GetString());
        });

    [Fact]
    public Task Proxy_catalog_not_found()
        => WithFactoryAsync(async client =>
        {
            var response = await client.GetAsync("/proxy/catalog/MISSING");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        });

    [Fact]
    public Task Client_info_endpoint_ok()
        => WithFactoryAsync(async client =>
        {
            var response = await client.GetAsync("/client-info");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });

    private sealed class Step10WebApplicationFactory(string catalogBaseUrl) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ExternalCatalog:BaseUrl", catalogBaseUrl);
        }
    }
}
