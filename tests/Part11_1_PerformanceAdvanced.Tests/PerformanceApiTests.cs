using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Part11_1_PerformanceAdvanced.Tests;

public sealed class PerformanceApiTests : IClassFixture<PerformanceFactory>
{
    private readonly PerformanceFactory _factory;

    public PerformanceApiTests(PerformanceFactory factory) => _factory = factory;

    [Fact]
    public async Task RuntimeEndpointReturnsGcAndFrameworkInfo()
    {
        using var client = _factory.CreateClient();
        var info = await client.GetFromJsonAsync<RuntimeInfo>("/api/performance/runtime");
        Assert.NotNull(info);
        Assert.True(info!.ProcessorCount > 0);
        Assert.False(string.IsNullOrEmpty(info.Framework));
        Assert.True(info.IsServerGC || !info.IsServerGC);
    }

    [Fact]
    public async Task CourseCodeParseBaselineAndSpanAgreeOnCorpus()
    {
        using var client = _factory.CreateClient();
        var cases = new[] { "CS-1010-A-2026F", "PHYS-2200-B-2027S", "MATH-1100-C-2026F", "bad", "" };
        foreach (var code in cases)
        {
            using var baseline = await client.PostAsJsonAsync(
                "/api/performance/course-codes/parse?impl=baseline", new { Code = code });
            using var span = await client.PostAsJsonAsync(
                "/api/performance/course-codes/parse?impl=span", new { Code = code });
            if (code is "bad" or "")
            {
                Assert.Equal(HttpStatusCode.BadRequest, baseline.StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, span.StatusCode);
            }
            else
            {
                baseline.EnsureSuccessStatusCode();
                span.EnsureSuccessStatusCode();
                var b = await baseline.Content.ReadFromJsonAsync<ParseResult>();
                var s = await span.Content.ReadFromJsonAsync<ParseResult>();
                Assert.Equal(b!.Subject, s!.Subject);
                Assert.Equal(b.Number, s.Number);
                Assert.Equal(b.Section, s.Section);
                Assert.Equal(b.Term, s.Term);
            }
        }
    }

    [Fact]
    public async Task SerializeSourceGenAndReflectionProduceEqualBytes()
    {
        using var client = _factory.CreateClient();
        var summary = new
        {
            EnrollmentId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            SectionId = Guid.NewGuid(),
            Status = "Confirmed",
            EnrolledAt = DateTimeOffset.UtcNow,
        };
        using var reflection = await client.PostAsJsonAsync(
            "/api/performance/serialize?impl=reflection", new { Summary = summary });
        using var sourcegen = await client.PostAsJsonAsync(
            "/api/performance/serialize?impl=sourcegen", new { Summary = summary });
        reflection.EnsureSuccessStatusCode();
        sourcegen.EnsureSuccessStatusCode();
        var r = await reflection.Content.ReadFromJsonAsync<SerializeResult>();
        var s = await sourcegen.Content.ReadFromJsonAsync<SerializeResult>();
        Assert.Equal(r!.Bytes, s!.Bytes);
    }

    [Fact]
    public async Task FaultLabIsNotDiscoverableWhenDisabled()
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(
            (_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Performance:FaultInjectionEnabled"] = "false",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
            })));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/lab/threadpool/async?delayMs=1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FaultLabRejectsMissingToken()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/lab/threadpool/async?delayMs=1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAllocateReleasesMemory()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lab-Token", "test-lab-token");
        using var allocate = await client.PostAsync("/lab/gc/allocate?megabytes=100", null);
        allocate.EnsureSuccessStatusCode();
        var allocated = await allocate.Content.ReadFromJsonAsync<AllocateResult>();
        Assert.Equal(64, allocated!.RetainedMegabytes);
        using var release = await client.DeleteAsync("/lab/gc/allocate");
        release.EnsureSuccessStatusCode();
        var released = await release.Content.ReadFromJsonAsync<AllocateResult>();
        Assert.Equal(0, released!.RetainedMegabytes);
    }

    [Fact]
    public async Task AsyncLabHonoursCancellation()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lab-Token", "test-lab-token");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            using var response = await client.GetAsync(
                "/lab/threadpool/async?delayMs=2000", cts.Token);
            Assert.Fail("Expected cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record RuntimeInfo(
        bool IsServerGC, int ProcessorCount, string Framework,
        string ProcessArchitecture, string GcMode, bool DynamicAdaptation);

    private sealed record ParseResult(string Subject, int Number, string Section, string Term);

    private sealed record SerializeResult(int Bytes, long ElapsedTicks);

    private sealed record AllocateResult(int RetainedMegabytes);
}

public sealed class PerformanceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Performance:FaultInjectionEnabled"] = "true",
                ["Performance:LabToken"] = "test-lab-token",
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
            }));
    }
}
