using System.Text.Json;
using Campus.Contracts;
using Npgsql;
using Part06_2_MessagingTools;

namespace Part07_DistributedComm;

public sealed class CatalogStore(IConfiguration configuration)
{
    public static readonly Guid SeedCourseId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private string ConnectionString =>
        configuration.GetConnectionString("Catalog")
        ?? throw new InvalidOperationException("ConnectionStrings:Catalog is required.");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS catalog_courses (
                id UUID PRIMARY KEY,
                code TEXT NOT NULL UNIQUE,
                title TEXT NOT NULL,
                available_seats INTEGER NOT NULL CHECK (available_seats >= 0)
            );
            INSERT INTO catalog_courses (id, code, title, available_seats)
            VALUES (@id, 'CS-CAPSTONE', 'Distributed Systems Capstone', 30)
            ON CONFLICT (id) DO UPDATE SET
                code = EXCLUDED.code,
                title = EXCLUDED.title,
                available_seats = EXCLUDED.available_seats;
            """;
        command.Parameters.AddWithValue("id", SeedCourseId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CatalogCourse?> GetAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, code, title, available_seats
            FROM catalog_courses
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("id", courseId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CatalogCourse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3))
            : null;
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return true;
    }
}

public sealed record CatalogCourse(
    Guid Id,
    string Code,
    string Title,
    int AvailableSeats);

public sealed class EnrollmentStore(IConfiguration configuration)
{
    private string ConnectionString =>
        configuration.GetConnectionString("Enrollment")
        ?? throw new InvalidOperationException("ConnectionStrings:Enrollment is required.");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS distributed_enrollments (
                id UUID PRIMARY KEY,
                student_id UUID NOT NULL,
                course_id UUID NOT NULL,
                course_code TEXT NOT NULL,
                requested_by TEXT NOT NULL,
                correlation_id TEXT NOT NULL,
                status TEXT NOT NULL,
                created_on_utc TIMESTAMPTZ NOT NULL,
                UNIQUE (student_id, course_id)
            );
            CREATE TABLE IF NOT EXISTS distributed_outbox (
                id UUID PRIMARY KEY,
                type TEXT NOT NULL,
                content JSONB NOT NULL,
                occurred_on_utc TIMESTAMPTZ NOT NULL,
                processed_on_utc TIMESTAMPTZ NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_distributed_outbox_pending
            ON distributed_outbox (processed_on_utc, occurred_on_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DistributedEnrollment> CreateAsync(
        Guid studentId,
        CatalogCourse course,
        string requestedBy,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var enrollment = new DistributedEnrollment(
            Guid.NewGuid(),
            studentId,
            course.Id,
            course.Code,
            requestedBy,
            correlationId,
            "Pending",
            DateTimeOffset.UtcNow);
        var message = new RabbitLabMessage(
            Guid.NewGuid(),
            enrollment.Id,
            IntegrationEventNames.EnrollmentConfirmedV1Name,
            correlationId,
            JsonSerializer.Serialize(new
            {
                enrollment.Id,
                enrollment.StudentId,
                enrollment.CourseId,
                enrollment.CourseCode,
                enrollment.RequestedBy,
            }),
            "none",
            0);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insertEnrollment = connection.CreateCommand())
        {
            insertEnrollment.Transaction = transaction;
            insertEnrollment.CommandText = """
                INSERT INTO distributed_enrollments
                    (id, student_id, course_id, course_code, requested_by,
                     correlation_id, status, created_on_utc)
                VALUES
                    (@id, @student_id, @course_id, @course_code, @requested_by,
                     @correlation_id, @status, @created_on_utc)
                """;
            insertEnrollment.Parameters.AddWithValue("id", enrollment.Id);
            insertEnrollment.Parameters.AddWithValue("student_id", enrollment.StudentId);
            insertEnrollment.Parameters.AddWithValue("course_id", enrollment.CourseId);
            insertEnrollment.Parameters.AddWithValue("course_code", enrollment.CourseCode);
            insertEnrollment.Parameters.AddWithValue("requested_by", enrollment.RequestedBy);
            insertEnrollment.Parameters.AddWithValue("correlation_id", enrollment.CorrelationId);
            insertEnrollment.Parameters.AddWithValue("status", enrollment.Status);
            insertEnrollment.Parameters.AddWithValue("created_on_utc", enrollment.CreatedOnUtc);
            await insertEnrollment.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertOutbox = connection.CreateCommand())
        {
            insertOutbox.Transaction = transaction;
            insertOutbox.CommandText = """
                INSERT INTO distributed_outbox
                    (id, type, content, occurred_on_utc)
                VALUES
                    (@id, @type, @content::jsonb, @occurred_on_utc)
                """;
            insertOutbox.Parameters.AddWithValue("id", message.MessageId);
            insertOutbox.Parameters.AddWithValue("type", message.Type);
            insertOutbox.Parameters.AddWithValue(
                "content",
                JsonSerializer.Serialize(message));
            insertOutbox.Parameters.AddWithValue(
                "occurred_on_utc",
                enrollment.CreatedOnUtc);
            await insertOutbox.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return enrollment;
    }

    public async Task<IReadOnlyList<DistributedEnrollment>> ListAsync(
        CancellationToken cancellationToken)
    {
        var result = new List<DistributedEnrollment>();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, student_id, course_id, course_code, requested_by,
                   correlation_id, status, created_on_utc
            FROM distributed_enrollments
            ORDER BY created_on_utc, id
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DistributedEnrollment(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return result;
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return true;
    }

    public string GetConnectionString() => ConnectionString;
}

public sealed record DistributedEnrollment(
    Guid Id,
    Guid StudentId,
    Guid CourseId,
    string CourseCode,
    string RequestedBy,
    string CorrelationId,
    string Status,
    DateTimeOffset CreatedOnUtc);
