using Npgsql;

namespace Part12_ElectiveBranches;

public sealed class InboxStore
{
    private readonly string? _connectionString;

    public InboxStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Notifications");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return;
        }
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS kafka_inbox (
                enrollment_id UUID PRIMARY KEY,
                processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS email_jobs (
                job_id UUID PRIMARY KEY,
                recipient TEXT NOT NULL,
                subject TEXT NOT NULL,
                html_body TEXT NOT NULL,
                text_body TEXT,
                state TEXT NOT NULL DEFAULT 'scheduled',
                attempts INT NOT NULL DEFAULT 0,
                provider_message_id TEXT,
                scheduled_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                completed_at TIMESTAMPTZ,
                idempotency_key TEXT UNIQUE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> MarkProcessedAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return true;
        }
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO kafka_inbox (enrollment_id) VALUES (@id) ON CONFLICT DO NOTHING RETURNING enrollment_id";
        command.Parameters.AddWithValue("id", enrollmentId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }
}
