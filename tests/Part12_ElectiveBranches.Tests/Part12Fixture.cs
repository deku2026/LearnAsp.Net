using System.Net.Sockets;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Part12_ElectiveBranches.Tests;

public sealed class Part12Fixture : IAsyncLifetime
{
    private IContainer? _kafkaContainer;
    private IContainer? _mailpitContainer;
    private PostgreSqlContainer? _postgresContainer;

    public string PostgresConnectionString { get; private set; } = "";
    public string KafkaBootstrap { get; private set; } = "";
    public int MailpitSmtpPort { get; private set; }
    public int MailpitApiPort { get; private set; }
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var pgEnv = Environment.GetEnvironmentVariable("CAMPUS_W9_PG");
        var kafkaEnv = Environment.GetEnvironmentVariable("CAMPUS_W9_KAFKA");
        var mailpitEnv = Environment.GetEnvironmentVariable("CAMPUS_W9_MAILPIT_API");

        var pgOk = await ConfigurePostgresAsync(pgEnv);
        var kafkaOk = await ConfigureKafkaAsync(kafkaEnv);
        var mailpitOk = await ConfigureMailpitAsync(mailpitEnv);

        if (!pgOk || !kafkaOk || !mailpitOk)
        {
            IsAvailable = false;
            return;
        }

        await EnsureDatabaseExistsAsync(PostgresConnectionString);
        IsAvailable = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_kafkaContainer is not null) await _kafkaContainer.DisposeAsync();
        if (_mailpitContainer is not null) await _mailpitContainer.DisposeAsync();
        if (_postgresContainer is not null) await _postgresContainer.DisposeAsync();
    }

    public Part12Factory CreateFactory() => new(PostgresConnectionString, KafkaBootstrap, MailpitSmtpPort);

    private async Task<bool> ConfigurePostgresAsync(string? envConn)
    {
        if (!string.IsNullOrWhiteSpace(envConn))
        {
            PostgresConnectionString = ReplaceDatabase(envConn, "campus_w9_test");
            return true;
        }
        try
        {
            _postgresContainer = new PostgreSqlBuilder("postgres:18.4-alpine")
                .WithDatabase("campus_w9_test")
                .WithUsername("dotnet")
                .WithPassword("dotnet_dev")
                .Build();
            await _postgresContainer.StartAsync();
            PostgresConnectionString = _postgresContainer.GetConnectionString();
            return true;
        }
        catch (Exception ex) when (IsInfrastructureUnavailable(ex))
        {
            SkipReason = $"PostgreSQL unavailable: {ex.Message}";
            _postgresContainer = null;
            return false;
        }
    }

    private async Task<bool> ConfigureKafkaAsync(string? envBootstrap)
    {
        if (!string.IsNullOrWhiteSpace(envBootstrap))
        {
            KafkaBootstrap = envBootstrap;
            return true;
        }
        try
        {
            _kafkaContainer = new ContainerBuilder("apache/kafka:4.3.1")
                .WithPortBinding(9094, true)
                .WithEnvironment("KAFKA_NODE_ID", "1")
                .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
                .WithEnvironment("KAFKA_LISTENERS", "PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093,EXTERNAL://0.0.0.0:9094")
                .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "PLAINTEXT://localhost:9092,EXTERNAL://localhost:9094")
                .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
                .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
                .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,EXTERNAL:PLAINTEXT")
                .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
                .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
                .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "true")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
                .Build();
            await _kafkaContainer.StartAsync();
            KafkaBootstrap = $"localhost:{_kafkaContainer.GetMappedPublicPort(9094)}";
            return true;
        }
        catch (Exception ex) when (IsInfrastructureUnavailable(ex))
        {
            SkipReason = $"Kafka unavailable: {ex.Message}";
            _kafkaContainer = null;
            return false;
        }
    }

    private async Task<bool> ConfigureMailpitAsync(string? envApi)
    {
        if (!string.IsNullOrWhiteSpace(envApi))
        {
            MailpitApiPort = int.Parse(envApi, System.Globalization.CultureInfo.InvariantCulture);
            MailpitSmtpPort = MailpitApiPort == 8025 ? 1125 : 1025;
            return true;
        }
        try
        {
            _mailpitContainer = new ContainerBuilder("axllent/mailpit:latest")
                .WithPortBinding(1025, true)
                .WithPortBinding(8025, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8025))
                .Build();
            await _mailpitContainer.StartAsync();
            MailpitSmtpPort = _mailpitContainer.GetMappedPublicPort(1025);
            MailpitApiPort = _mailpitContainer.GetMappedPublicPort(8025);
            return true;
        }
        catch (Exception ex) when (IsInfrastructureUnavailable(ex))
        {
            SkipReason = $"Mailpit unavailable: {ex.Message}";
            _mailpitContainer = null;
            return false;
        }
    }

    private static bool IsInfrastructureUnavailable(Exception ex) =>
        ex is DockerUnavailableException or DockerImageNotFoundException or DockerApiException
            or SocketException or TimeoutException or IOException;

    private static string ReplaceDatabase(string conn, string db)
    {
        var builder = new NpgsqlConnectionStringBuilder(conn) { Database = db };
        return builder.ConnectionString;
    }

    private static async Task EnsureDatabaseExistsAsync(string conn)
    {
        var builder = new NpgsqlConnectionStringBuilder(conn);
        var dbName = builder.Database ?? throw new InvalidOperationException("DB name required");
        builder.Database = "postgres";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
        exists.Parameters.AddWithValue("name", dbName);
        if (await exists.ExecuteScalarAsync() is not null) return;
        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{dbName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        await create.ExecuteNonQueryAsync();
    }
}

public sealed class Part12Factory(
    string postgresConn,
    string kafkaBootstrap,
    int mailpitSmtpPort) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Notifications"] = postgresConn,
            ["ConnectionStrings:Kafka"] = kafkaBootstrap,
            ["Kafka:RunConsumer"] = "true",
            ["Kafka:GroupId"] = $"campus-w9-test-{Guid.NewGuid():N}",
            ["Notifications:RunScheduler"] = "true",
            ["Notifications:SmtpHost"] = "localhost",
            ["Notifications:SmtpPort"] = mailpitSmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Notifications:PollIntervalMs"] = "500",
            ["Notifications:BaseBackoffMs"] = "50",
            ["Notifications:MaxBackoffMs"] = "500",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
        }));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Part12Collection : ICollectionFixture<Part12Fixture>
{
    public const string Name = "w12-electives";
}
