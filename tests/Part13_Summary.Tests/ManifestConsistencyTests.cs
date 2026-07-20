using System.Text.Json;

namespace Part13_Summary.Tests;

public sealed class ManifestConsistencyTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task CapabilitiesManifestHas31LabsNoDuplicatesUniquePorts()
    {
        var path = Path.Combine(RepoRoot, "docs", "summary", "capabilities.json");
        var json = await File.ReadAllTextAsync(path);
        var caps = JsonSerializer.Deserialize(json, SummaryJsonContext.Default.CapabilitiesFile);
        Assert.NotNull(caps);
        Assert.Equal(31, caps!.Labs.Count);
        var ids = caps.Labs.Select(l => l.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var ports = caps.Labs.Select(l => l.Port).ToList();
        Assert.Equal(ports.Count, ports.Distinct().Count());
        Assert.All(ports, p => Assert.InRange(p, 5001, 5031));
    }

    [Fact]
    public async Task EveryLabHasTestProjectOrManualEvidence()
    {
        var path = Path.Combine(RepoRoot, "docs", "summary", "capabilities.json");
        var json = await File.ReadAllTextAsync(path);
        var caps = JsonSerializer.Deserialize(json, SummaryJsonContext.Default.CapabilitiesFile);
        Assert.NotNull(caps);
        foreach (var lab in caps!.Labs)
        {
            Assert.False(string.IsNullOrWhiteSpace(lab.TestProject),
                $"Lab {lab.Id} has no testProject or manual evidence");
        }
    }

    [Fact]
    public async Task InfrastructureManifestHasTenContainers()
    {
        var path = Path.Combine(RepoRoot, "docs", "summary", "infrastructure.json");
        var json = await File.ReadAllTextAsync(path);
        var infra = JsonSerializer.Deserialize(json, SummaryJsonContext.Default.InfrastructureFile);
        Assert.NotNull(infra);
        Assert.Equal(10, infra!.Containers.Count);
        var names = infra.Containers.Select(c => c.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public async Task CapstonesManifestHasThreeCapstones()
    {
        var path = Path.Combine(RepoRoot, "docs", "summary", "capstones.json");
        var json = await File.ReadAllTextAsync(path);
        var capstones = JsonSerializer.Deserialize(json, SummaryJsonContext.Default.CapstonesFile);
        Assert.NotNull(capstones);
        Assert.Equal(3, capstones!.Capstones.Count);
        var ids = capstones.Capstones.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}