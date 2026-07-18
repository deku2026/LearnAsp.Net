using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Respawn;
using Step09_IntegrationTesting.Data;
using Testcontainers.PostgreSql;

namespace Step09_IntegrationTesting.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;
    private string _connectionString = "";

    public string ConnectionString => _connectionString;
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        // 1) Prefer Testcontainers when Docker works
        try
        {
            _container = new PostgreSqlBuilder("postgres:18.4-alpine")
                .WithDatabase("campus_step09_it")
                .WithUsername("dotnet")
                .WithPassword("dotnet_dev")
                .Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // 2) Fallback: existing local stack (WSL docker-compose on localhost:5432)
            var fallback = Environment.GetEnvironmentVariable("CAMPUS_TEST_PG")
                           ?? "Host=localhost;Port=5432;Database=campus_step09_it;Username=dotnet;Password=dotnet_dev";
            try
            {
                await EnsureDatabaseExistsAsync(fallback);
                await using var conn = new NpgsqlConnection(fallback);
                await conn.OpenAsync();
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT 1";
                    await cmd.ExecuteScalarAsync();
                }

                _connectionString = fallback;
                IsAvailable = true;
            }
            catch (Exception fallbackEx)
            {
                IsAvailable = false;
                SkipReason =
                    $"PostgreSQL unavailable for integration tests. Testcontainers: {ex.GetType().Name}: {ex.Message}; Fallback: {fallbackEx.Message}";
                return;
            }
        }

        var efOptions = new DbContextOptionsBuilder<CampusDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using (var db = new CampusDbContext(efOptions))
        {
            await db.Database.EnsureCreatedAsync();
        }

        await using var respawnConn = new NpgsqlConnection(_connectionString);
        await respawnConn.OpenAsync();
        _respawner = await Respawner.CreateAsync(respawnConn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
        });
    }

    public WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // UseSetting is applied early enough for top-level Program configuration reads.
            builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _connectionString,
                });
            });
        });
    }

    public async Task ResetAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        if (_respawner is not null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await _respawner.ResetAsync(conn);
        }

        // Guarantee empty tables even if Respawn mapping misses a relation
        await using var wipe = new NpgsqlConnection(_connectionString);
        await wipe.OpenAsync();
        await using var cmd = wipe.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE enrollments, sections, courses RESTART IDENTITY CASCADE";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var dbName = builder.Database;
        if (string.IsNullOrWhiteSpace(dbName))
        {
            return;
        }

        builder.Database = "postgres";
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();
        await using (var exists = conn.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @n";
            exists.Parameters.AddWithValue("n", dbName);
            var found = await exists.ExecuteScalarAsync();
            if (found is not null)
            {
                return;
            }
        }

        await using var create = conn.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{dbName.Replace("\"", "\"\"")}\"";
        await create.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
