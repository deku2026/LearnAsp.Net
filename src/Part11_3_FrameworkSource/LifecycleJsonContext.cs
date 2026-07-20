using System.Text.Json.Serialization;

namespace Part11_3_FrameworkSource;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScopedIdDto))]
[JsonSerializable(typeof(PipelineTraceDto))]
[JsonSerializable(typeof(MetadataReadDto))]
[JsonSerializable(typeof(OptionsChangeDto))]
[JsonSerializable(typeof(AuthPathDto))]
[JsonSerializable(typeof(LifecycleTraceDto))]
public partial class LifecycleJsonContext : JsonSerializerContext;
