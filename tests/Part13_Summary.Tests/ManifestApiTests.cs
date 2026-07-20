using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Part13_Summary.Tests;

public sealed class ManifestApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ManifestApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task CapabilitiesEndpointReturnsAll31Labs()
    {
        using var client = _factory.CreateClient();
        var caps = await client.GetFromJsonAsync<CapabilitiesFile>("/api/capabilities", CamelCase);
        Assert.NotNull(caps);
        Assert.Equal(31, caps!.Labs.Count);
    }

    [Fact]
    public async Task CapstonesEndpointReturnsThreeCapstones()
    {
        using var client = _factory.CreateClient();
        var capstones = await client.GetFromJsonAsync<CapstonesFile>("/api/capstones", CamelCase);
        Assert.NotNull(capstones);
        Assert.Equal(3, capstones!.Capstones.Count);
    }

    [Fact]
    public async Task InfrastructureEndpointReturnsTenContainers()
    {
        using var client = _factory.CreateClient();
        var infra = await client.GetFromJsonAsync<InfrastructureFile>("/api/infrastructure", CamelCase);
        Assert.NotNull(infra);
        Assert.Equal(10, infra!.Containers.Count);
    }

    [Fact]
    public async Task EvidenceEndpointReturnsAutoAndManual()
    {
        using var client = _factory.CreateClient();
        var evidence = await client.GetFromJsonAsync<EvidenceFile>("/api/evidence", CamelCase);
        Assert.NotNull(evidence);
        Assert.NotEmpty(evidence!.AutoEvidence);
        Assert.NotEmpty(evidence.ManualEvidence);
    }

    private sealed record CapabilitiesFile(IReadOnlyList<CapabilityLab> Labs);
    private sealed record CapabilityLab(string Id, string Name, int Port, string Wave, string Status, string Doc, string TestProject);
    private sealed record CapstonesFile(IReadOnlyList<Capstone> Capstones);
    private sealed record Capstone(string Id, string Name, string Wave, string Milestone, IReadOnlyList<string> Contents);
    private sealed record InfrastructureFile(IReadOnlyList<InfrastructureContainer> Containers);
    private sealed record InfrastructureContainer(string Name, string Image, int HostPort, string Purpose, string Acceptance, bool RealUse);
    private sealed record EvidenceFile(IReadOnlyList<EvidenceEntry> AutoEvidence, IReadOnlyList<EvidenceManualEntry> ManualEvidence);
    private sealed record EvidenceEntry(string Lab, string Method, string Platform);
    private sealed record EvidenceManualEntry(string Lab, string Method, string Document);
}