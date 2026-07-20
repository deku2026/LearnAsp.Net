namespace Part12_ElectiveBranches;

public sealed record ScheduleEmailRequest(
    string Recipient,
    string Subject,
    string HtmlBody,
    string? TextBody,
    string? IdempotencyKey);

public sealed record EmailJobStatus(
    Guid JobId,
    string State,
    int Attempts,
    string? ProviderMessageId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? CompletedAt);

public sealed record KafkaProduceResult(
    Guid EnrollmentId,
    string Topic,
    int Partition,
    long Offset);

public sealed record KafkaStatusDto(
    string Topic,
    int Partitions,
    long ConsumerLag,
    string ConsumerGroup);