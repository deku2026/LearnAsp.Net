using System.Net.Sockets;
using Campus.Testing;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Part04_1_EFCore;
using Testcontainers.PostgreSql;

namespace Part04_1_EFCore.Tests;

public sealed class PgFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = "";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        // 1) Try Testcontainers (works on Linux CI with Docker)
        try
        {
            _container = new PostgreSqlBuilder("postgres:18.4-alpine")
                .WithDatabase("campus_w5_test")
                .WithUsername("dotnet")
                .WithPassword("dotnet_dev")
                .Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (DockerUnavailableException ex)
        {
            SkipReason = $"Testcontainers Docker unavailable: {ex.Message}";
            _container = null;
        }
        catch (DockerImageNotFoundException ex)
        {
            SkipReason = $"Testcontainers image missing: {ex.Message}";
            _container = null;
        }
        catch (DockerApiException ex)
        {
            SkipReason = $"Testcontainers Docker API error: {ex.Message}";
            _container = null;
        }
        catch (TimeoutException ex)
        {
            SkipReason = $"Testcontainers timed out: {ex.Message}";
            _container = null;
        }
        catch (IOException ex)
        {
            SkipReason = $"Testcontainers IO: {ex.Message}";
            _container = null;
        }
        catch (SocketException ex)
        {
            SkipReason = $"Testcontainers socket: {ex.Message}";
            _container = null;
        }
        catch (HttpRequestException ex)
        {
            SkipReason = $"Testcontainers HTTP: {ex.Message}";
            _container = null;
        }

        // 2) Fallback: local PG on localhost:5432
        if (_container is null)
        {
            ConnectionString = Environment.GetEnvironmentVariable("CAMPUS_TEST_PG")
                               ?? "Host=localhost;Port=5432;Database=campus_w5_test;Username=dotnet;Password=dotnet_dev";
            try
            {
                await EnsureDatabaseExistsAsync(ConnectionString);
                await using var conn = new NpgsqlConnection(ConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync();
                SkipReason = null;
            }
            catch (NpgsqlException ex)
            {
                IsAvailable = false;
                SkipReason = $"PostgreSQL unavailable: {ex.Message}";
                return;
            }
            catch (SocketException ex)
            {
                IsAvailable = false;
                SkipReason = $"Socket: {ex.Message}";
                return;
            }
        }

        // 3) Migrate
        try
        {
            var options = new DbContextOptionsBuilder<CampusDbContext>()
                .UseNpgsql(ConnectionString).Options;
            await using var db = new CampusDbContext(options);
            try
            {
                await db.Database.MigrateAsync();
            }
            catch (NpgsqlException)
            {
                await db.Database.EnsureDeletedAsync();
                await db.Database.MigrateAsync();
            }
            IsAvailable = true;
        }
        catch (NpgsqlException ex)
        {
            IsAvailable = false;
            SkipReason = $"Migration failed: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            IsAvailable = false;
            SkipReason = $"Migration invalid op: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Campus", ConnectionString);
            b.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Campus"] = ConnectionString,
                });
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (!IsAvailable) return;
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE attendance_records, enrollments, sections, courses RESTART IDENTITY CASCADE";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var dbName = builder.Database;
        if (string.IsNullOrWhiteSpace(dbName)) return;
        builder.Database = "postgres";
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await using (var exists = conn.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @n";
            exists.Parameters.AddWithValue("n", dbName);
            if (await exists.ExecuteScalarAsync() is not null) return;
        }
        await using var create = conn.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await create.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("pg")]
public sealed class PgCollection : ICollectionFixture<PgFixture> { }

public static class Skip
{
    public static void IfNotAvailable(PgFixture fx)
    {
        if (!fx.IsAvailable)
            global::Xunit.Skip.If(fx.SkipReason is not null, fx.SkipReason ?? "PostgreSQL unavailable");
    }
}
