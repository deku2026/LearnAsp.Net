using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Part08_1_OpenTelemetry.Tests;

public sealed class OpenTelemetryApiTests :
    IClassFixture<OpenTelemetryApiTests.TelemetryFactory>
{
    private readonly TelemetryFactory _factory;

    public OpenTelemetryApiTests(TelemetryFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WorkEndpointReturnsTraceIdentityAndPropagatesW3CContext()
    {
        using var client = _factory.CreateClient();
        var workId = Guid.NewGuid();

        using var response = await client.GetAsync(
            $"/api/observability/work/{workId}?delayMs=10");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WorkResponse>();
        Assert.Equal(workId, result!.WorkId);
        Assert.Matches("^[0-9a-f]{32}$", result.TraceId);
        Assert.Matches("^[0-9a-f]{16}$", result.SpanId);
    }

    [Fact]
    public async Task FailureEndpointUsesProblemDetailsAndExposesTraceId()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/observability/failure");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        var traceId = Assert.IsType<JsonElement>(
            problem!.Extensions["activityTraceId"]);
        Assert.Matches("^[0-9a-f]{32}$", traceId.GetString()!);
    }

    public sealed class TelemetryFactory : WebApplicationFactory<Program>
    {
        private readonly WireMockServer _downstream;

        public TelemetryFactory()
        {
            _downstream = WireMockServer.Start();
            _downstream
                .Given(Request.Create()
                    .WithPath("/api/downstream/*")
                    .WithHeader(
                        "traceparent",
                        new RegexMatcher(
                            "^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$"))
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """{"workId":"00000000-0000-0000-0000-000000000000","delayMs":10,"status":"completed"}"""));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Troubleshooting:BaseUrl"] = _downstream.Url,
                        ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
                    }));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _downstream.Stop();
                _downstream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed record WorkResponse(Guid WorkId, string TraceId, string SpanId);
}
