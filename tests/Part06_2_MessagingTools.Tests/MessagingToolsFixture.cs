using System.Net.Sockets;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Part06_2_MessagingTools.Tests;

public sealed class MessagingToolsFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private IContainer? _rabbitContainer;

    public string PostgresConnectionString { get; private set; } = "";

    public string RabbitMqConnectionString { get; private set; } = "";

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!await ConfigurePostgresAsync() || !await ConfigureRabbitMqAsync())
        {
            return;
        }

        await EnsureDatabaseExistsAsync(PostgresConnectionString);
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        IsAvailable = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_rabbitContainer is not null)
        {
            await _rabbitContainer.DisposeAsync();
        }

        if (_postgresContainer is not null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    public W7ToolsFactory CreateFactory() =>
        new(PostgresConnectionString, RabbitMqConnectionString);

    private async Task<bool> ConfigurePostgresAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CAMPUS_W7_PG");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            PostgresConnectionString = ReplaceDatabase(configured, "campus_w7_tools_test");
            return true;
        }

        try
        {
            _postgresContainer = new PostgreSqlBuilder("postgres:18.4-alpine")
                .WithDatabase("campus_w7_tools_test")
                .WithUsername("dotnet")
                .WithPassword("dotnet_dev")
                .Build();
            await _postgresContainer.StartAsync();
            PostgresConnectionString = _postgresContainer.GetConnectionString();
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
            _postgresContainer = null;
            return false;
        }
    }

    private async Task<bool> ConfigureRabbitMqAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CAMPUS_W7_RABBITMQ");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            RabbitMqConnectionString = configured;
            return true;
        }

        try
        {
            _rabbitContainer = new ContainerBuilder("rabbitmq:4.3.2-management-alpine")
                .WithEnvironment("RABBITMQ_DEFAULT_USER", "dotnet")
                .WithEnvironment("RABBITMQ_DEFAULT_PASS", "dotnet_dev")
                .WithPortBinding(5672, true)
                .WithWaitStrategy(
                    Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
                .Build();
            await _rabbitContainer.StartAsync();
            RabbitMqConnectionString =
                $"amqp://dotnet:dotnet_dev@127.0.0.1:{_rabbitContainer.GetMappedPublicPort(5672)}/";
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
            SkipReason = $"RabbitMQ Testcontainer unavailable: {ex.Message}";
            _rabbitContainer = null;
            return false;
        }
    }

    private static string ReplaceDatabase(
        string connectionString,
        string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = database,
        };
        return builder.ConnectionString;
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database
            ?? throw new InvalidOperationException("PostgreSQL database name is required.");
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
}

public sealed class W7ToolsFactory(
    string postgresConnectionString,
    string rabbitMqConnectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Messaging"] = postgresConnectionString,
                ["ConnectionStrings:RabbitMQ"] = rabbitMqConnectionString,
                ["RabbitMQ:RunConsumer"] = "true",
                ["RabbitMQ:RetryDelayMilliseconds"] = "50",
                ["RabbitMQ:MaxDeliveryAttempts"] = "3",
                ["RabbitMQ:Prefetch"] = "4",
            });
        });
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MessagingToolsCollection
    : ICollectionFixture<MessagingToolsFixture>
{
    public const string Name = "w7-tools";
}
