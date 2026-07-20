using Npgsql;

namespace Part12_ElectiveBranches;

public sealed class EmailJobStore
{
    private readonly string? _connectionString;

    public EmailJobStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Notifications");
    }

    public string GetConnectionString() => _connectionString ?? "";

    public async Task<Guid> ScheduleAsync(
        string recipient,
        string subject,
        string htmlBody,
        string? textBody,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return Guid.NewGuid();
        }
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH ins AS (
                INSERT INTO email_jobs (job_id, recipient, subject, html_body, text_body, idempotency_key)
                VALUES (@jobId, @recipient, @subject, @html, @text, @idemKey)
                ON CONFLICT (idempotency_key) DO NOTHING
                RETURNING job_id
            )
            SELECT job_id FROM ins
            UNION ALL
            SELECT job_id FROM email_jobs WHERE idempotency_key = @idemKey
            LIMIT 1;
            """;
        var jobId = Guid.NewGuid();
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("recipient", recipient);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("html", htmlBody);
        command.Parameters.AddWithValue("text", (object?)textBody ?? DBNull.Value);
        command.Parameters.AddWithValue("idemKey", (object?)idempotencyKey ?? DBNull.Value);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (Guid)result!;
    }

    public async Task<EmailJobStatus?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT job_id, state, attempts, provider_message_id, scheduled_at, completed_at FROM email_jobs WHERE job_id = @id";
        command.Parameters.AddWithValue("id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new EmailJobStatus(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }

    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE email_jobs SET state = 'cancelled' WHERE job_id = @id AND state = 'scheduled'";
        command.Parameters.AddWithValue("id", jobId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<EmailJobRow?> AcquireNextAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return null;
        }
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (NpgsqlTransaction)transaction;
            command.CommandText = """
                SELECT job_id, recipient, subject, html_body, text_body, attempts
                FROM email_jobs
                WHERE state = 'scheduled'
                ORDER BY scheduled_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            var row = new EmailJobRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5));
            await reader.DisposeAsync();
            await using var update = connection.CreateCommand();
            update.Transaction = (NpgsqlTransaction)transaction;
            update.CommandText = "UPDATE email_jobs SET state = 'running', attempts = attempts + 1 WHERE job_id = @id";
            update.Parameters.AddWithValue("id", row.JobId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return row;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task MarkCompletedAsync(Guid jobId, string providerMessageId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE email_jobs SET state = 'completed', provider_message_id = @mid, completed_at = now() WHERE job_id = @id";
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("mid", providerMessageId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE email_jobs SET state = 'scheduled' WHERE job_id = @id AND state = 'running'";
        command.Parameters.AddWithValue("id", jobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TruncateAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE email_jobs; TRUNCATE kafka_inbox;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record EmailJobRow(
    Guid JobId,
    string Recipient,
    string Subject,
    string HtmlBody,
    string? TextBody,
    int Attempts);