using System.Text.Json;
using Campus.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Part06_1_MessagingPatterns;

public sealed class InboxProcessor(
    MessagingDbContext db,
    TimeProvider timeProvider)
{
    public async Task<InboxResult> ReceiveAsync(
        ReceiveInboxMessageRequest request,
        CancellationToken cancellationToken)
    {
        const string consumer = "campus-notifications";
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        var receivedOnUtc = timeProvider.GetUtcNow();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inbox_messages
                (message_id, consumer, type, content, received_on_utc, processed_on_utc)
            VALUES
                ({request.MessageId}, {consumer}, {request.Type}, {request.Payload}::jsonb,
                 {receivedOnUtc}, NULL)
            ON CONFLICT (message_id, consumer) DO NOTHING
            """, cancellationToken);

        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new InboxResult(false, true, request.MessageId);
        }

        db.NotificationReceipts.Add(new NotificationReceipt
        {
            Id = Guid.NewGuid(),
            MessageId = request.MessageId,
            EnrollmentId = request.EnrollmentId,
            CreatedOnUtc = timeProvider.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await db.InboxMessages
            .Where(message =>
                message.MessageId == request.MessageId &&
                message.Consumer == consumer)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedOnUtc,
                    timeProvider.GetUtcNow()),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InboxResult(true, false, request.MessageId);
    }
}

public sealed class SagaOrchestrator(
    MessagingDbContext db,
    TimeProvider timeProvider)
{
    public async Task<EnrollmentSaga> StartAsync(
        Guid enrollmentId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var saga = new EnrollmentSaga
        {
            Id = Guid.NewGuid(),
            EnrollmentId = enrollmentId,
            State = SagaStates.AwaitingPayment,
            CreatedOnUtc = now,
            UpdatedOnUtc = now,
        };
        db.Sagas.Add(saga);
        AddOutbox(
            IntegrationEventNames.PaymentReserveRequested,
            new { saga.Id, saga.EnrollmentId });
        await db.SaveChangesAsync(cancellationToken);
        return saga;
    }

    public async Task<EnrollmentSaga?> RecordPaymentAsync(
        Guid sagaId,
        SagaStepResult result,
        CancellationToken cancellationToken)
    {
        var saga = await db.Sagas.SingleOrDefaultAsync(
            row => row.Id == sagaId,
            cancellationToken);
        if (saga is null)
        {
            return null;
        }

        EnsureState(saga, SagaStates.AwaitingPayment);
        if (!result.Succeeded)
        {
            saga.State = SagaStates.Failed;
            saga.FailureReason = result.Reason ?? "Payment reservation failed.";
            AddOutbox(
                IntegrationEventNames.EnrollmentCancelledV1Name,
                new { saga.Id, saga.EnrollmentId, saga.FailureReason });
        }
        else
        {
            saga.PaymentReserved = true;
            saga.State = SagaStates.AwaitingSeat;
            AddOutbox(
                IntegrationEventNames.SeatReserveRequested,
                new { saga.Id, saga.EnrollmentId });
        }

        saga.UpdatedOnUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return saga;
    }

    public async Task<EnrollmentSaga?> RecordSeatAsync(
        Guid sagaId,
        SagaStepResult result,
        CancellationToken cancellationToken)
    {
        var saga = await db.Sagas.SingleOrDefaultAsync(
            row => row.Id == sagaId,
            cancellationToken);
        if (saga is null)
        {
            return null;
        }

        EnsureState(saga, SagaStates.AwaitingSeat);
        if (result.Succeeded)
        {
            saga.SeatReserved = true;
            saga.State = SagaStates.Completed;
            AddOutbox(
                IntegrationEventNames.EnrollmentConfirmedV1Name,
                new { saga.Id, saga.EnrollmentId });
        }
        else
        {
            saga.State = SagaStates.CompensatingPayment;
            saga.FailureReason = result.Reason ?? "Seat reservation failed.";
            AddOutbox(
                IntegrationEventNames.PaymentRefundRequested,
                new { saga.Id, saga.EnrollmentId, saga.FailureReason });
        }

        saga.UpdatedOnUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return saga;
    }

    public async Task<EnrollmentSaga?> CompleteCompensationAsync(
        Guid sagaId,
        CancellationToken cancellationToken)
    {
        var saga = await db.Sagas.SingleOrDefaultAsync(
            row => row.Id == sagaId,
            cancellationToken);
        if (saga is null)
        {
            return null;
        }

        EnsureState(saga, SagaStates.CompensatingPayment);
        saga.PaymentReserved = false;
        saga.State = SagaStates.Compensated;
        saga.UpdatedOnUtc = timeProvider.GetUtcNow();
        AddOutbox(
            IntegrationEventNames.EnrollmentCancelledV1Name,
            new { saga.Id, saga.EnrollmentId, saga.FailureReason });
        await db.SaveChangesAsync(cancellationToken);
        return saga;
    }

    private void AddOutbox(string type, object data)
    {
        var now = timeProvider.GetUtcNow();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            ContentJson = JsonSerializer.Serialize(data),
            OccurredOnUtc = now,
            NextAttemptOnUtc = now,
        });
    }

    private static void EnsureState(EnrollmentSaga saga, string expected)
    {
        if (!string.Equals(saga.State, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Saga {saga.Id} is '{saga.State}', expected '{expected}'.");
        }
    }
}
