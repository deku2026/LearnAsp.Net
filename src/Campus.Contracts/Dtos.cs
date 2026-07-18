namespace Campus.Contracts;

public sealed record CourseDto(Guid Id, string Code, string Title, int Credits);

public sealed record CreateCourseRequest(string Code, string Title, int Credits);

public sealed record SectionDto(
    Guid Id,
    Guid CourseId,
    string Term,
    int Capacity,
    int SeatsRemaining);

public sealed record CreateSectionRequest(Guid CourseId, string Term, int Capacity);

public sealed record EnrollmentDto(
    Guid Id,
    Guid StudentId,
    Guid SectionId,
    EnrollmentStatus Status,
    DateTimeOffset CreatedAt);

public sealed record CreateEnrollmentRequest(Guid StudentId, Guid SectionId);

public sealed record EnvInfoDto(string EnvironmentName, string ContentRootPath, string ApplicationName);
