using System.Net.Sockets;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Part06_1_MessagingPatterns.Tests;

public sealed class MessagingPatternsFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = "";

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CAMPUS_W7_PG");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            ConnectionString = WithDatabase(
                configured,
                "campus_w7_patterns_test");
            await EnsureDatabaseExistsAsync(ConnectionString);
        }
        else if (!await TryStartContainerAsync())
        {
            return;
        }

        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using var database = new MessagingDbContext(options);
        await database.Database.MigrateAsync();
        IsAvailable = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public W7PatternsFactory CreateFactory() =>
        new(ConnectionString);

    public async Task ResetAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            TRUNCATE TABLE
                notification_receipts,
                inbox_messages,
                dead_letter_messages,
                outbox_messages,
                enrollment_sagas,
                enrollment_records
            RESTART IDENTITY CASCADE
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> TryStartContainerAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:18.4-alpine")
                .WithDatabase("campus_w7_patterns_test")
                .WithUsername("dotnet")
                .WithPassword("dotnet_dev")
                .Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
            return true;
        }
        catch (Exception ex) when (
            ex is DockerUnavailableException or
                DockerImageNotFoundException or
                DockerApiException or
                SocketException or
                TimeoutException or
                IOException or
                HttpRequestException)
        {
            SkipReason = $"PostgreSQL Testcontainer unavailable: {ex.Message}";
            _container = null;
            return false;
        }
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("CAMPUS_W7_PG must include a database name.");
        }

        builder.Database = "postgres";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
            exists.Parameters.AddWithValue("name", databaseName);
            if (await exists.ExecuteScalarAsync() is not null)
            {
                return;
            }
        }

        await using var create = connection.CreateCommand();
        create.CommandText =
            $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        await create.ExecuteNonQueryAsync();
    }

    private static string WithDatabase(
        string connectionString,
        string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database,
        };
        return builder.ConnectionString;
    }
}

public sealed class W7PatternsFactory(string connectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Messaging", connectionString);
        builder.UseSetting("Messaging:RunRelay", "false");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Messaging"] = connectionString,
                ["Messaging:RunRelay"] = "false",
                ["Messaging:MaxDeliveryAttempts"] = "3",
                ["Database:ApplyMigrations"] = "false",
            });
        });
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MessagingPatternsCollection
    : ICollectionFixture<MessagingPatternsFixture>
{
    public const string Name = "w7-patterns";
}
