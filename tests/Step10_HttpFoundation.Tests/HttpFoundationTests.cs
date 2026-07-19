using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
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

    public ValueTask InitializeAsync()
    {
        _wireMock = WireMockServer.Start();
        _wireMock
            .Given(Request.Create()
                .WithPath("/catalog/CS101")
                .WithHeader("X-Correlation-ID", "corr-123")
                .WithHeader("X-Catalog-Key", "test-catalog-key")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"code":"CS101","title":"Intro","provider":"wiremock"}"""));

        _wireMock
            .Given(Request.Create().WithPath("/catalog/MISSING").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        _wireMock
            .Given(Request.Create().WithPath("/catalog/RETRY").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        _wireMock
            .Given(Request.Create().WithPath("/catalog/BREAK").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        _wireMock
            .Given(Request.Create().WithPath("/catalog/SLOW").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(2000)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"code":"SLOW","title":"Slow","provider":"wiremock"}"""));

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _wireMock.Stop();
        _wireMock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task WithFactoryAsync(
        Func<HttpClient, Task> test,
        int attemptTimeoutMs = 3000,
        int totalTimeoutMs = 10000,
        int circuitMinimumThroughput = 100)
    {
        var baseUrl = _wireMock.Url!.TrimEnd('/') + "/";
        await using var factory = new Step10WebApplicationFactory(
            baseUrl,
            attemptTimeoutMs,
            totalTimeoutMs,
            circuitMinimumThroughput);
        var client = factory.CreateClient();
        await test(client);
    }

    [Fact]
    public Task Kestrel_limits_endpoint_reports_configured_values()
        => WithFactoryAsync(async client =>
        {
            var json = await client.GetFromJsonAsync<JsonElement>("/kestrel-limits");
            Assert.Equal(256, json.GetProperty("maxConcurrentConnections").GetInt32());
            Assert.Equal(1024, json.GetProperty("maxRequestBodyBytes").GetInt64());
        });

    [Fact]
    public Task Proxy_catalog_returns_external_payload()
        => WithFactoryAsync(async client =>
        {
            client.DefaultRequestHeaders.Add("X-Correlation-ID", "corr-123");
            var response = await client.GetAsync("/proxy/catalog/CS101");
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            var json = JsonDocument.Parse(body).RootElement;
            Assert.Equal("CS101", json.GetProperty("code").GetString());
            Assert.Equal("wiremock", json.GetProperty("provider").GetString());
        });

    [Fact]
    public Task Standard_resilience_retries_transient_500_twice()
        => WithFactoryAsync(async client =>
        {
            var response = await client.GetAsync("/proxy/catalog/RETRY");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var attempts = _wireMock.LogEntries.Count(entry =>
                string.Equals(entry.RequestMessage?.Path, "/catalog/RETRY", StringComparison.Ordinal));
            Assert.Equal(3, attempts);
        });

    [Fact]
    public Task Standard_resilience_attempt_timeout_stops_slow_upstream()
        => WithFactoryAsync(
            async client =>
            {
                var timer = Stopwatch.StartNew();
                var response = await client.GetAsync("/proxy/catalog/SLOW");
                timer.Stop();
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"elapsed: {timer.Elapsed}");
            },
            attemptTimeoutMs: 100,
            totalTimeoutMs: 500);

    [Fact]
    public Task Standard_resilience_circuit_breaker_short_circuits_repeated_failure()
        => WithFactoryAsync(
            async client =>
            {
                Assert.Equal(
                    HttpStatusCode.InternalServerError,
                    (await client.GetAsync("/proxy/catalog/BREAK")).StatusCode);
                var attemptsAfterFirstCall = _wireMock.LogEntries.Count(entry =>
                    string.Equals(entry.RequestMessage?.Path, "/catalog/BREAK", StringComparison.Ordinal));
                Assert.True(attemptsAfterFirstCall >= 2);

                Assert.Equal(
                    HttpStatusCode.InternalServerError,
                    (await client.GetAsync("/proxy/catalog/BREAK")).StatusCode);
                var attemptsAfterSecondCall = _wireMock.LogEntries.Count(entry =>
                    string.Equals(entry.RequestMessage?.Path, "/catalog/BREAK", StringComparison.Ordinal));
                Assert.Equal(attemptsAfterFirstCall, attemptsAfterSecondCall);
            },
            circuitMinimumThroughput: 2);

    [Fact]
    public Task Forwarded_headers_apply_only_for_known_loopback_proxy()
        => WithFactoryAsync(async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/remote-ip");
            request.Headers.Add("X-Forwarded-For", "203.0.113.42");
            request.Headers.Add("X-Forwarded-Proto", "https");
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("203.0.113.42", json.GetProperty("remoteIp").GetString());
            Assert.Equal("https", json.GetProperty("scheme").GetString());
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

    [Fact]
    public async Task Real_kestrel_rejects_oversized_body_and_endpoint_override_allows_it()
    {
        var port = GetFreeTcpPort();
        var assemblyPath = typeof(Program).Assembly.Location;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{assemblyPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.Environment["Kestrel__Endpoints__Http__Url"] = $"http://127.0.0.1:{port}";
        process.StartInfo.Environment["Kestrel__Endpoints__Http__Protocols"] = "Http1";
        process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        Assert.True(process.Start());
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            await WaitUntilReadyAsync(client);
            using var payload = new StringContent(new string('x', 2048), Encoding.UTF8, "text/plain");
            var rejected = await client.PostAsync("/upload", payload);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);

            using var allowedPayload = new StringContent(new string('x', 2048), Encoding.UTF8, "text/plain");
            var allowed = await client.PostAsync("/upload/large", allowedPayload);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitUntilReadyAsync(HttpClient client)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                if ((await client.GetAsync("/")).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Kestrel has not bound the port yet.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The real Kestrel process did not become ready.");
    }

    private sealed class Step10WebApplicationFactory(
        string catalogBaseUrl,
        int attemptTimeoutMs,
        int totalTimeoutMs,
        int circuitMinimumThroughput) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ExternalCatalog:BaseUrl", catalogBaseUrl);
            builder.UseSetting("ExternalCatalog:ApiKey", "test-catalog-key");
            builder.UseSetting("Resilience:AttemptTimeoutMs", attemptTimeoutMs.ToString());
            builder.UseSetting("Resilience:TotalTimeoutMs", totalTimeoutMs.ToString());
            builder.UseSetting("Resilience:CircuitMinimumThroughput", circuitMinimumThroughput.ToString());
        }
    }
}
