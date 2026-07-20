namespace Part12_ElectiveBranches;

public static class KafkaTopics
{
    public const string EnrollmentActivity = "campus.enrollment.activity.v1";
    public const string EnrollmentActivityDlq = "campus.enrollment.activity.dlq.v1";
}

public sealed record EnrollmentActivityEvent(
    Guid EnrollmentId,
    Guid StudentId,
    Guid SectionId,
    string Status,
    DateTimeOffset OccurredAt);
