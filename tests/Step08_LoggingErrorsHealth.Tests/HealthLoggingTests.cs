using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;

namespace Step08_LoggingErrorsHealth.Tests;

public sealed class HealthLoggingTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthLoggingTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_is_healthy()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_reflects_gate()
    {
        var ok = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        await _client.PostAsJsonAsync("/ready-state", new { ready = false });
        var bad = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, bad.StatusCode);

        await _client.PostAsJsonAsync("/ready-state", new { ready = true });
        var restored = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public async Task Boom_returns_problem_details()
    {
        var response = await _client.GetAsync("/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("title", out _));
        if (json.TryGetProperty("errorCode", out var code))
        {
            Assert.Equal(ErrorCodes.InternalError, code.GetString());
        }
        else if (json.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("errorCode", out var nested))
        {
            Assert.Equal(ErrorCodes.InternalError, nested.GetString());
        }
    }

    [Fact]
    public async Task Correlation_header_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Correlation-ID", "corr-test-1");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Contains("corr-test-1", values);
    }
}
