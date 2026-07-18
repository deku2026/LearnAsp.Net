using System.Net;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;

namespace Step03_MiddlewarePipeline.Tests;

public sealed class MiddlewareTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public MiddlewareTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ok_has_elapsed_header()
    {
        var response = await _client.GetAsync("/ok");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Elapsed-ms"));
    }

    [Fact]
    public async Task Boom_returns_problem_with_error_code()
    {
        var response = await _client.GetAsync("/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(ErrorCodes.InternalError, doc.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Branch_map_terminates()
    {
        var text = await _client.GetStringAsync("/branch");
        Assert.Equal("branch-terminal", text);
    }
}
