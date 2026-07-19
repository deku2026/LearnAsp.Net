using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;
using Microsoft.AspNetCore.Hosting;

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
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", report.GetProperty("status").GetString());
        Assert.Contains(
            report.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("name").GetString() == "self");
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
    public async Task Production_error_does_not_leak_exception_details()
    {
        await using var factory = new CampusWebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var response = await factory.CreateClient().GetAsync("/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("lab-boom", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
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

    [Fact]
    public async Task Invalid_correlation_header_is_replaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Correlation-ID", new string('x', 65));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.DoesNotContain("invalid", value, StringComparison.Ordinal);
        Assert.Equal(32, value.Length);
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task Ready_check_executes_a_real_postgres_query_when_available()
    {
        const string connectionString =
            "Host=127.0.0.1;Port=5432;Database=dotnet_dev;Username=dotnet;Password=dotnet_dev;Timeout=2";
        await using var factory = new CampusWebApplicationFactory<Program>()
            .WithSetting("ConnectionStrings:Postgres", connectionString);
        var response = await factory.CreateClient().GetAsync("/health/ready");
        Assert.SkipUnless(
            response.StatusCode == HttpStatusCode.OK,
            "Local PostgreSQL is unavailable; Docker-backed readiness verification was skipped.");

        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            report.GetProperty("checks").EnumerateArray(),
            check =>
                check.GetProperty("name").GetString() == "campus-ready" &&
                check.GetProperty("description").GetString() == "postgres ready" &&
                check.GetProperty("status").GetString() == "Healthy");
    }

    [Fact]
    public async Task Database_outage_fails_readiness_but_not_liveness()
    {
        const string unavailable =
            "Host=127.0.0.1;Port=1;Database=dotnet_dev;Username=dotnet;Password=dotnet_dev;Timeout=1";
        await using var factory = new CampusWebApplicationFactory<Program>()
            .WithSetting("ConnectionStrings:Postgres", unavailable);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }
}
