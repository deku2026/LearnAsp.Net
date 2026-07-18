using System.Net;
using System.Net.Http.Json;
using Campus.Contracts;
using Campus.Testing;

namespace Step01_HostStartup.Tests;

public sealed class HostStartupTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HostStartupTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_returns_ok()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Env_returns_environment_name()
    {
        var info = await _client.GetFromJsonAsync<EnvInfoDto>("/env");
        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.EnvironmentName));
        Assert.False(string.IsNullOrWhiteSpace(info.ContentRootPath));
    }

    [Fact]
    public async Task Heartbeat_endpoint_is_reachable()
    {
        var response = await _client.GetAsync("/heartbeat-count");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
