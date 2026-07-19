using Npgsql;

namespace Part06_2_MessagingTools;

public sealed class RabbitInboxStore(IConfiguration configuration)
{
    private string ConnectionString =>
        configuration.GetConnectionString("Messaging")
        ?? "Host=localhost;Port=5432;Database=campus_w7_tools;Username=dotnet;Password=dotnet_dev";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS rabbit_inbox (
                message_id UUID PRIMARY KEY,
                type TEXT NOT NULL,
                received_on_utc TIMESTAMPTZ NOT NULL,
                processed_on_utc TIMESTAMPTZ NOT NULL
            );
            CREATE TABLE IF NOT EXISTS rabbit_notifications (
                id UUID PRIMARY KEY,
                message_id UUID NOT NULL UNIQUE,
                enrollment_id UUID NOT NULL,
                payload TEXT NOT NULL,
                delivery_attempt INTEGER NOT NULL,
                created_on_utc TIMESTAMPTZ NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryHandleAsync(
        RabbitLabMessage message,
        int deliveryAttempt,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var inbox = connection.CreateCommand();
        inbox.Transaction = transaction;
        inbox.CommandText = """
            INSERT INTO rabbit_inbox
                (message_id, type, received_on_utc, processed_on_utc)
            VALUES
                (@message_id, @type, now(), now())
            ON CONFLICT (message_id) DO NOTHING
            """;
        inbox.Parameters.AddWithValue("message_id", message.MessageId);
        inbox.Parameters.AddWithValue("type", message.Type);
        var inserted = await inbox.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await using var notification = connection.CreateCommand();
        notification.Transaction = transaction;
        notification.CommandText = """
            INSERT INTO rabbit_notifications
                (id, message_id, enrollment_id, payload, delivery_attempt, created_on_utc)
            VALUES
                (@id, @message_id, @enrollment_id, @payload, @delivery_attempt, now())
            """;
        notification.Parameters.AddWithValue("id", Guid.NewGuid());
        notification.Parameters.AddWithValue("message_id", message.MessageId);
        notification.Parameters.AddWithValue("enrollment_id", message.EnrollmentId);
        notification.Parameters.AddWithValue("payload", message.Payload);
        notification.Parameters.AddWithValue("delivery_attempt", deliveryAttempt);
        await notification.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RabbitNotification>> ListAsync(
        CancellationToken cancellationToken)
    {
        var result = new List<RabbitNotification>();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, message_id, enrollment_id, payload, delivery_attempt, created_on_utc
            FROM rabbit_notifications
            ORDER BY created_on_utc, id
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new RabbitNotification(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return result;
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE TABLE rabbit_notifications, rabbit_inbox";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record RabbitNotification(
    Guid Id,
    Guid MessageId,
    Guid EnrollmentId,
    string Payload,
    int DeliveryAttempt,
    DateTimeOffset CreatedOnUtc);
