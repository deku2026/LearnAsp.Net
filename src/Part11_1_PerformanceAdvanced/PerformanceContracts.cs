namespace Part11_1_PerformanceAdvanced;

public sealed record RuntimeInfoDto(
    bool IsServerGC,
    int ProcessorCount,
    string Framework,
    string ProcessArchitecture,
    string GcMode,
    bool DynamicAdaptation);

public sealed record CourseCodeParseResult(
    string Subject,
    int Number,
    string Section,
    string Term);

public sealed record EnrollmentSummaryDto(
    Guid EnrollmentId,
    Guid StudentId,
    Guid SectionId,
    string Status,
    DateTimeOffset EnrolledAt);

public sealed record SerializeResultDto(
    int Bytes,
    long ElapsedTicks);

public sealed record PayloadDto(string Content, int Length);

public sealed record ParseRequest(string Code);

public sealed record SerializeRequest(EnrollmentSummaryDto Summary);
