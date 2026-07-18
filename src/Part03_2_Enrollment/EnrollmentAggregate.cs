using Campus.Contracts;

namespace Part03_2.Enrollment;

/// <summary>Rich model only where invariants matter: one active enrollment path + status transitions.</summary>
public sealed class EnrollmentAggregate
{
    private EnrollmentAggregate()
    {
    }

    public static EnrollmentAggregate Create(Guid studentId, Guid sectionId, bool seatReserved)
    {
        return new EnrollmentAggregate
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            SectionId = sectionId,
            Status = seatReserved ? EnrollmentStatus.Confirmed : EnrollmentStatus.Waitlisted,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid SectionId { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsConfirmed => Status == EnrollmentStatus.Confirmed;
}
