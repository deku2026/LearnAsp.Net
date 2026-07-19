using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Hello from Development", payload.GetProperty("greeting").GetString());
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

    [Fact]
    public async Task Heartbeat_increments_over_time_and_reports_scoped_id()
    {
        // Allow the hosted service to tick at least once.
        await Task.Delay(1500);

        var first = await _client.GetFromJsonAsync<JsonElement>("/heartbeat-count");
        var firstCount = first.GetProperty("count").GetInt64();
        var firstHasScoped = first.GetProperty("lastScopedId").ValueKind == JsonValueKind.String;
        var firstScopedId = firstHasScoped ? first.GetProperty("lastScopedId").GetGuid() : Guid.Empty;

        await Task.Delay(2200);

        var second = await _client.GetFromJsonAsync<JsonElement>("/heartbeat-count");
        var secondCount = second.GetProperty("count").GetInt64();
        var secondHasScoped = second.GetProperty("lastScopedId").ValueKind == JsonValueKind.String;
        var secondScopedId = secondHasScoped ? second.GetProperty("lastScopedId").GetGuid() : Guid.Empty;

        Assert.True(secondCount > firstCount, $"expected second {secondCount} > first {firstCount}");
        // Each tick creates a new scoped TickRecorder via IServiceScopeFactory — ids differ.
        Assert.True(secondHasScoped, "second tick should have a scoped id");
        Assert.True(firstHasScoped, "first tick should have a scoped id");
        Assert.NotEqual(firstScopedId, secondScopedId);
    }

    [Fact]
    public async Task Webroot_endpoint_returns_paths()
    {
        var info = await _client.GetFromJsonAsync<JsonElement>("/webroot");
        Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("contentRoot").GetString()));
    }
}
