using System.Net;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Step03_MiddlewarePipeline.Tests;

public sealed class MiddlewareTests
{
    [Fact]
    public async Task Ok_has_elapsed_header()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/ok");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Elapsed-ms"));
    }

    [Fact]
    public async Task Boom_returns_problem_with_error_code()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(ErrorCodes.InternalError, doc.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Branch_map_terminates()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var text = await client.GetStringAsync("/branch");
        Assert.Equal("branch-terminal", text);
    }

    [Fact]
    public async Task Factory_middleware_adds_request_id_header()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var r1 = await client.GetAsync("/ok");
        var r2 = await client.GetAsync("/ok");
        Assert.True(r1.Headers.Contains("X-Factory-MW-Request-Id"));
        Assert.True(r2.Headers.Contains("X-Factory-MW-Request-Id"));
        // Per-request activation: different scoped RequestContext instances.
        Assert.NotEqual(
            r1.Headers.GetValues("X-Factory-MW-Request-Id").First(),
            r2.Headers.GetValues("X-Factory-MW-Request-Id").First());
    }

    [Fact]
    public async Task UseWhen_rejoins_main_pipeline()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/diag");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Branch header set by UseWhen.
        Assert.True(response.Headers.Contains("X-Diag-Branch"));
        // Main pipeline timing header also present (UseWhen rejoined).
        Assert.True(response.Headers.Contains("X-Elapsed-ms"));
    }

    [Fact]
    public async Task MapWhen_terminates_without_rejoining()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/terminal-diag");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("terminal-diag-branch", body);
        // MapWhen is terminal: UseWhen's branch header NOT present (different branch).
        Assert.False(response.Headers.Contains("X-Diag-Branch"));
    }

    [Fact]
    public async Task Short_circuit_returns_404_for_favicon()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/favicon.ico");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Auth_order_experiment_breaks_whoami()
    {
        // With the WRONG order (authz before authn), /whoami returns 401 even though
        // FakeAuthHandler always authenticates. The correct order would return 200.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Lab:AuthOrderExperiment", "true");
        });
        var client = factory.CreateClient();
        var response = await client.GetAsync("/whoami");
        // With wrong order, authorization fails because authentication hasn't run yet.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Correct_auth_order_allows_whoami()
    {
        await using var factory = new CampusWebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/whoami");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}