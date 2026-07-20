using System.Text.Json;

namespace Part13_Summary;

public sealed class ManifestLoader
{
    private readonly CapabilitiesFile? _capabilities;
    private readonly CapstonesFile? _capstones;
    private readonly InfrastructureFile? _infrastructure;
    private readonly EvidenceFile? _evidence;

    public ManifestLoader(IWebHostEnvironment environment)
    {
        var candidates = new[]
        {
            Path.Combine(environment.ContentRootPath, "manifests"),
            Path.Combine(AppContext.BaseDirectory, "manifests"),
            Path.Combine(environment.ContentRootPath, "..", "..", "..", "..", "..", "docs", "summary"),
        };
        var manifestsDir = candidates.FirstOrDefault(Directory.Exists)
            ?? Path.Combine(environment.ContentRootPath, "manifests");
        _capabilities = Load<CapabilitiesFile>(Path.Combine(manifestsDir, "capabilities.json"));
        _capstones = Load<CapstonesFile>(Path.Combine(manifestsDir, "capstones.json"));
        _infrastructure = Load<InfrastructureFile>(Path.Combine(manifestsDir, "infrastructure.json"));
        _evidence = Load<EvidenceFile>(Path.Combine(manifestsDir, "evidence.json"));
    }

    public CapabilitiesFile? Capabilities => _capabilities;
    public CapstonesFile? Capstones => _capstones;
    public InfrastructureFile? Infrastructure => _infrastructure;
    public EvidenceFile? Evidence => _evidence;

    private static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }
        var json = File.ReadAllText(path);
        var typeInfo = SummaryJsonContext.Default.GetTypeInfo(typeof(T));
        if (typeInfo is null)
        {
            return null;
        }
        return JsonSerializer.Deserialize(json, typeInfo) as T;
    }
}
