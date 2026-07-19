using Microsoft.EntityFrameworkCore;

namespace Part06_1_MessagingPatterns;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options)
    : DbContext(options)
{
    public DbSet<EnrollmentRecord> Enrollments => Set<EnrollmentRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<NotificationReceipt> NotificationReceipts => Set<NotificationReceipt>();

    public DbSet<DeadLetterMessage> DeadLetters => Set<DeadLetterMessage>();

    public DbSet<EnrollmentSaga> Sagas => Set<EnrollmentSaga>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EnrollmentRecord>(entity =>
        {
            entity.ToTable("enrollment_records");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.StudentId).HasColumnName("student_id");
            entity.Property(row => row.SectionId).HasColumnName("section_id");
            entity.Property(row => row.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(row => row.CreatedOnUtc).HasColumnName("created_on_utc");
            entity.HasIndex(row => new { row.StudentId, row.SectionId }).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).HasColumnName("id");
            entity.Property(message => message.Type).HasColumnName("type").HasMaxLength(200);
            entity.Property(message => message.ContentJson).HasColumnName("content").HasColumnType("jsonb");
            entity.Property(message => message.OccurredOnUtc).HasColumnName("occurred_on_utc");
            entity.Property(message => message.ProcessedOnUtc).HasColumnName("processed_on_utc");
            entity.Property(message => message.NextAttemptOnUtc).HasColumnName("next_attempt_on_utc");
            entity.Property(message => message.Attempts).HasColumnName("attempts");
            entity.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(1000);
            entity.Property(message => message.FailureMode).HasColumnName("failure_mode").HasMaxLength(32);
            entity.Property(message => message.FailuresBeforeSuccess)
                .HasColumnName("failures_before_success");
            entity.HasIndex(message => new
            {
                message.ProcessedOnUtc,
                message.NextAttemptOnUtc,
                message.OccurredOnUtc,
            });
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(message => new { message.MessageId, message.Consumer });
            entity.Property(message => message.MessageId).HasColumnName("message_id");
            entity.Property(message => message.Consumer).HasColumnName("consumer").HasMaxLength(200);
            entity.Property(message => message.Type).HasColumnName("type").HasMaxLength(200);
            entity.Property(message => message.ContentJson).HasColumnName("content").HasColumnType("jsonb");
            entity.Property(message => message.ReceivedOnUtc).HasColumnName("received_on_utc");
            entity.Property(message => message.ProcessedOnUtc).HasColumnName("processed_on_utc");
        });

        modelBuilder.Entity<NotificationReceipt>(entity =>
        {
            entity.ToTable("notification_receipts");
            entity.HasKey(receipt => receipt.Id);
            entity.Property(receipt => receipt.Id).HasColumnName("id");
            entity.Property(receipt => receipt.MessageId).HasColumnName("message_id");
            entity.Property(receipt => receipt.EnrollmentId).HasColumnName("enrollment_id");
            entity.Property(receipt => receipt.CreatedOnUtc).HasColumnName("created_on_utc");
            entity.HasIndex(receipt => receipt.MessageId).IsUnique();
        });

        modelBuilder.Entity<DeadLetterMessage>(entity =>
        {
            entity.ToTable("dead_letter_messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).HasColumnName("id");
            entity.Property(message => message.OriginalMessageId).HasColumnName("original_message_id");
            entity.Property(message => message.Type).HasColumnName("type").HasMaxLength(200);
            entity.Property(message => message.ContentJson).HasColumnName("content").HasColumnType("jsonb");
            entity.Property(message => message.Reason).HasColumnName("reason").HasMaxLength(1000);
            entity.Property(message => message.FailedOnUtc).HasColumnName("failed_on_utc");
            entity.Property(message => message.Attempts).HasColumnName("attempts");
            entity.HasIndex(message => message.OriginalMessageId).IsUnique();
        });

        modelBuilder.Entity<EnrollmentSaga>(entity =>
        {
            entity.ToTable("enrollment_sagas");
            entity.HasKey(saga => saga.Id);
            entity.Property(saga => saga.Id).HasColumnName("id");
            entity.Property(saga => saga.EnrollmentId).HasColumnName("enrollment_id");
            entity.Property(saga => saga.State).HasColumnName("state").HasMaxLength(40);
            entity.Property(saga => saga.PaymentReserved).HasColumnName("payment_reserved");
            entity.Property(saga => saga.SeatReserved).HasColumnName("seat_reserved");
            entity.Property(saga => saga.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
            entity.Property(saga => saga.CreatedOnUtc).HasColumnName("created_on_utc");
            entity.Property(saga => saga.UpdatedOnUtc).HasColumnName("updated_on_utc");
            entity.Property<uint>("xmin").IsRowVersion();
            entity.HasIndex(saga => saga.EnrollmentId).IsUnique();
        });
    }
}

public sealed class EnrollmentRecord
{
    public Guid Id { get; set; }

    public Guid StudentId { get; set; }

    public Guid SectionId { get; set; }

    public string Status { get; set; } = "Pending";

    public DateTimeOffset CreatedOnUtc { get; set; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public string Type { get; set; } = "";

    public string ContentJson { get; set; } = "";

    public DateTimeOffset OccurredOnUtc { get; set; }

    public DateTimeOffset? ProcessedOnUtc { get; set; }

    public DateTimeOffset NextAttemptOnUtc { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public string FailureMode { get; set; } = "none";

    public int FailuresBeforeSuccess { get; set; }
}

public sealed class InboxMessage
{
    public Guid MessageId { get; set; }

    public string Consumer { get; set; } = "";

    public string Type { get; set; } = "";

    public string ContentJson { get; set; } = "";

    public DateTimeOffset ReceivedOnUtc { get; set; }

    public DateTimeOffset? ProcessedOnUtc { get; set; }
}

public sealed class NotificationReceipt
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    public Guid EnrollmentId { get; set; }

    public DateTimeOffset CreatedOnUtc { get; set; }
}

public sealed class DeadLetterMessage
{
    public Guid Id { get; set; }

    public Guid OriginalMessageId { get; set; }

    public string Type { get; set; } = "";

    public string ContentJson { get; set; } = "";

    public string Reason { get; set; } = "";

    public DateTimeOffset FailedOnUtc { get; set; }

    public int Attempts { get; set; }
}

public sealed class EnrollmentSaga
{
    public Guid Id { get; set; }

    public Guid EnrollmentId { get; set; }

    public string State { get; set; } = SagaStates.AwaitingPayment;

    public bool PaymentReserved { get; set; }

    public bool SeatReserved { get; set; }

    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedOnUtc { get; set; }

    public DateTimeOffset UpdatedOnUtc { get; set; }
}

public static class SagaStates
{
    public const string AwaitingPayment = "AwaitingPayment";
    public const string AwaitingSeat = "AwaitingSeat";
    public const string CompensatingPayment = "CompensatingPayment";
    public const string Completed = "Completed";
    public const string Compensated = "Compensated";
    public const string Failed = "Failed";
}
