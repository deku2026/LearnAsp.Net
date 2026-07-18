using Campus.Contracts;

namespace Part03_2.Enrollment.Contracts;

public interface IEnrollmentModule
{
    Task<EnrollmentDto> EnrollAsync(Guid studentId, Guid sectionId, CancellationToken ct = default);
    Task<IReadOnlyList<EnrollmentDto>> ListAsync(Guid? studentId = null, CancellationToken ct = default);
}

public sealed record EnrollmentDto(Guid Id, Guid StudentId, Guid SectionId, EnrollmentStatus Status, DateTimeOffset CreatedAt);

public sealed record EnrollmentConfirmed(Guid EnrollmentId, Guid StudentId, Guid SectionId, DateTimeOffset OccurredAt);
