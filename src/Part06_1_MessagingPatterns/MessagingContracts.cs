namespace Part06_1_MessagingPatterns;

public sealed record CreateEnrollmentCommand(Guid StudentId, Guid SectionId);

public sealed record QueueLabMessageRequest(
    string Type,
    string Payload,
    string FailureMode = "none",
    int FailuresBeforeSuccess = 0);

public sealed record ReceiveInboxMessageRequest(
    Guid MessageId,
    Guid EnrollmentId,
    string Type,
    string Payload);

public sealed record StartSagaRequest(Guid EnrollmentId);

public sealed record SagaStepResult(bool Succeeded, string? Reason = null);

public sealed record DispatchResult(
    int Claimed,
    int Published,
    int Retried,
    int DeadLettered);

public sealed record InboxResult(bool Accepted, bool Duplicate, Guid MessageId);
