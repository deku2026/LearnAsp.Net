using System.Text.Json;
using Npgsql;
using Part06_2_MessagingTools;

namespace Part07_DistributedComm;

public sealed class DistributedOutboxRelay(
    EnrollmentStore store,
    RabbitMqConnection rabbit,
    ILogger<DistributedOutboxRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException ex)
            {
                logger.LogWarning(ex, "Distributed outbox PostgreSQL iteration failed.");
            }
            catch (RabbitMQ.Client.Exceptions.RabbitMQClientException ex)
            {
                logger.LogWarning(ex, "Distributed outbox RabbitMQ publish failed.");
            }

            try
            {
                await Task.Delay(100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(store.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var messages = new List<OutboxRow>();
        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                SELECT id, content
                FROM distributed_outbox
                WHERE processed_on_utc IS NULL
                ORDER BY occurred_on_utc, id
                LIMIT 20
                FOR UPDATE SKIP LOCKED
                """;
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new OutboxRow(reader.GetGuid(0), reader.GetString(1)));
            }
        }

        foreach (var row in messages)
        {
            var message = JsonSerializer.Deserialize<RabbitLabMessage>(row.ContentJson)
                ?? throw new JsonException($"Outbox {row.Id} has no message body.");
            await rabbit.PublishAsync(message, cancellationToken);
            await using var mark = connection.CreateCommand();
            mark.Transaction = transaction;
            mark.CommandText = """
                UPDATE distributed_outbox
                SET processed_on_utc = now(), attempts = attempts + 1, last_error = NULL
                WHERE id = @id
                """;
            mark.Parameters.AddWithValue("id", row.Id);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record OutboxRow(Guid Id, string ContentJson);
}
