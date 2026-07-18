using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
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
        if (!await TryStartTestcontainerAsync() && !await TryConnectLocalPostgresAsync())
        {
            return;
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

    public async Task UsingFactoryAsync(Func<WebApplicationFactory<Program>, Task> test)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _connectionString,
                });
            });
        });
        await test(factory);
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

    private async Task<bool> TryStartTestcontainerAsync()
    {
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
            return true;
        }
        catch (DockerUnavailableException ex)
        {
            SkipReason = $"Testcontainers Docker unavailable: {ex.Message}";
            _container = null;
            return false;
        }
        catch (TimeoutException ex)
        {
            SkipReason = $"Testcontainers timed out: {ex.Message}";
            _container = null;
            return false;
        }
        catch (InvalidOperationException ex)
        {
            SkipReason = $"Testcontainers invalid operation: {ex.Message}";
            _container = null;
            return false;
        }
        catch (IOException ex)
        {
            SkipReason = $"Testcontainers IO failure: {ex.Message}";
            _container = null;
            return false;
        }
        catch (SocketException ex)
        {
            SkipReason = $"Testcontainers socket failure: {ex.Message}";
            _container = null;
            return false;
        }
        catch (ArgumentException ex)
        {
            SkipReason = $"Testcontainers configuration error: {ex.Message}";
            _container = null;
            return false;
        }
    }

    private async Task<bool> TryConnectLocalPostgresAsync()
    {
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
            SkipReason = null;
            return true;
        }
        catch (NpgsqlException ex)
        {
            IsAvailable = false;
            SkipReason = AppendFallback(SkipReason, $"local Postgres Npgsql: {ex.Message}");
            return false;
        }
        catch (SocketException ex)
        {
            IsAvailable = false;
            SkipReason = AppendFallback(SkipReason, $"local Postgres socket: {ex.Message}");
            return false;
        }
        catch (TimeoutException ex)
        {
            IsAvailable = false;
            SkipReason = AppendFallback(SkipReason, $"local Postgres timeout: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            IsAvailable = false;
            SkipReason = AppendFallback(SkipReason, $"local Postgres invalid op: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            IsAvailable = false;
            SkipReason = AppendFallback(SkipReason, $"local Postgres IO: {ex.Message}");
            return false;
        }
    }

    private static string AppendFallback(string? prior, string detail)
        => string.IsNullOrWhiteSpace(prior) ? detail : $"{prior}; Fallback: {detail}";

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
        create.CommandText = $"CREATE DATABASE \"{dbName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        await create.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
