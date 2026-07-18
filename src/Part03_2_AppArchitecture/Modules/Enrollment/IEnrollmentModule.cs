using Campus.Contracts;

namespace Part03_2_AppArchitecture.Modules.Enrollment;

public interface IEnrollmentModule
{
    Task<EnrollmentInfo> EnrollAsync(Guid studentId, Guid sectionId, CancellationToken ct = default);
    Task<IReadOnlyList<EnrollmentInfo>> ListAsync(Guid? studentId = null, CancellationToken ct = default);
}

public sealed record EnrollmentInfo(Guid Id, Guid StudentId, Guid SectionId, EnrollmentStatus Status, DateTimeOffset CreatedAt);

public sealed record EnrollmentConfirmed(Guid EnrollmentId, Guid StudentId, Guid SectionId, DateTimeOffset OccurredAt);
