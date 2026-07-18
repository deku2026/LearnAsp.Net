using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Part04_2_Caching;

namespace Part04_2_Caching.Tests;

public sealed class CacheFixture : IAsyncLifetime
{
    public string PgConnectionString { get; private set; } = "";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    private bool _migrated;

    public async Task InitializeAsync()
    {
        // Use unique env var per test project, fallback to CAMPUS_TEST_PG base, then default.
        PgConnectionString = Environment.GetEnvironmentVariable("CAMPUS_CACHE_TEST_PG")
                             ?? Environment.GetEnvironmentVariable("CAMPUS_TEST_PG")
                             ?? "Host=localhost;Port=5432;Database=campus_cache_test;Username=dotnet;Password=dotnet_dev";
        try
        {
            await EnsureDatabaseExistsAsync(PgConnectionString);
            await using var conn = new NpgsqlConnection(PgConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            // Migrate once — if tables exist from a previous run, drop and recreate.
            var options = new DbContextOptionsBuilder<CacheDbContext>()
                .UseNpgsql(PgConnectionString).Options;
            await using (var db = new CacheDbContext(options))
            {
                try
                {
                    await db.Database.MigrateAsync();
                }
                catch (NpgsqlException)
                {
                    // Stale schema — drop and retry
                    await db.Database.EnsureDeletedAsync();
                    await db.Database.MigrateAsync();
                }
            }
            _migrated = true;
            IsAvailable = true;
        }
        catch (NpgsqlException ex)
        {
            IsAvailable = false;
            SkipReason = $"PostgreSQL unavailable: {ex.Message}";
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            IsAvailable = false;
            SkipReason = $"Socket: {ex.Message}";
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Campus", PgConnectionString);
            b.UseSetting("ConnectionStrings:Redis", "127.0.0.1:6380,abortConnect=false");
            // Disable Redis L2 in tests (WSL2 network quirks; HybridCache L1 still works).
            // Redis resilience tests run separately on Linux CI with Docker.
            b.UseSetting("Cache:UseRedisL2", "false");
            b.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Campus"] = PgConnectionString,
                    ["ConnectionStrings:Redis"] = "127.0.0.1:6380,abortConnect=false",
                    ["Cache:UseRedisL2"] = "false",
                });
            });
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (!IsAvailable) return;
        // Fast reset: TRUNCATE instead of drop+migrate (saves ~37s per test).
        await using var conn = new NpgsqlConnection(PgConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE courses RESTART IDENTITY CASCADE";
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

[CollectionDefinition("cache")]
public sealed class CacheCollection : ICollectionFixture<CacheFixture> { }

public static class CacheSkip
{
    public static void IfNotAvailable(CacheFixture fx)
    {
        if (!fx.IsAvailable)
            global::Xunit.Skip.If(fx.SkipReason is not null, fx.SkipReason ?? "PostgreSQL unavailable");
    }
}
