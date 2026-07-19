namespace Campus.Contracts;

public static class IntegrationEventNames
{
    public const string EnrollmentRequested = "campus.enrollment.requested.v1";
    public const string EnrollmentConfirmed = "campus.enrollment.confirmed";
    public const string EnrollmentCancelled = "campus.enrollment.cancelled";
    public const string EnrollmentConfirmedV1Name = "campus.enrollment.confirmed.v1";
    public const string EnrollmentCancelledV1Name = "campus.enrollment.cancelled.v1";
    public const string PaymentReserveRequested = "campus.payment.reserve-requested.v1";
    public const string PaymentRefundRequested = "campus.payment.refund-requested.v1";
    public const string SeatReserveRequested = "campus.seat.reserve-requested.v1";
}

public sealed record MessageEnvelope<T>(
    Guid MessageId,
    string Type,
    int Version,
    DateTimeOffset OccurredOnUtc,
    string CorrelationId,
    T Data);

public sealed record EnrollmentRequestedV1(
    Guid EnrollmentId,
    Guid StudentId,
    Guid SectionId);

public sealed record EnrollmentConfirmedV1(
    Guid EnrollmentId,
    Guid StudentId,
    Guid SectionId);
