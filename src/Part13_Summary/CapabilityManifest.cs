using System.Text.Json.Serialization;

namespace Part13_Summary;

public sealed record CapabilityLab(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("wave")] string Wave,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("doc")] string Doc,
    [property: JsonPropertyName("testProject")] string TestProject);

public sealed record CapabilitiesFile(
    [property: JsonPropertyName("labs")] IReadOnlyList<CapabilityLab> Labs);

public sealed record Capstone(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("wave")] string Wave,
    [property: JsonPropertyName("milestone")] string Milestone,
    [property: JsonPropertyName("contents")] IReadOnlyList<string> Contents);

public sealed record CapstonesFile(
    [property: JsonPropertyName("capstones")] IReadOnlyList<Capstone> Capstones);

public sealed record InfrastructureContainer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("image")] string Image,
    [property: JsonPropertyName("hostPort")] int HostPort,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("acceptance")] string Acceptance,
    [property: JsonPropertyName("realUse")] bool RealUse);

public sealed record InfrastructureFile(
    [property: JsonPropertyName("containers")] IReadOnlyList<InfrastructureContainer> Containers);

public sealed record EvidenceEntry(
    [property: JsonPropertyName("lab")] string Lab,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("platform")] string Platform);

public sealed record EvidenceManualEntry(
    [property: JsonPropertyName("lab")] string Lab,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("document")] string Document);

public sealed record EvidenceFile(
    [property: JsonPropertyName("autoEvidence")] IReadOnlyList<EvidenceEntry> AutoEvidence,
    [property: JsonPropertyName("manualEvidence")] IReadOnlyList<EvidenceManualEntry> ManualEvidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CapabilitiesFile))]
[JsonSerializable(typeof(CapstonesFile))]
[JsonSerializable(typeof(InfrastructureFile))]
[JsonSerializable(typeof(EvidenceFile))]
public partial class SummaryJsonContext : JsonSerializerContext;