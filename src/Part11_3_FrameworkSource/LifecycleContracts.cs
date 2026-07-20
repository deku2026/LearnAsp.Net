namespace Part11_3_FrameworkSource;

public sealed record ScopedIdDto(Guid RequestId, Guid ScopeId, string ScopeHash);

public sealed record PipelineTraceDto(IReadOnlyList<string> Before, IReadOnlyList<string> After);

public sealed record MetadataReadDto(string Policy, string Requirement, bool Present);

public sealed record OptionsChangeDto(string Source, string Value, int Changes);

public sealed record AuthPathDto(string Path, string Scheme, bool Authenticated);

public sealed record LifecycleTraceDto(IReadOnlyList<string> Stages);

public sealed class LifecycleOptions
{
    public string DemoValue { get; set; } = "initial";
}
