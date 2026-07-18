using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Sockets;
using Campus.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Part04_1_EFCore;

namespace Part04_1_EFCore.Tests;

public sealed class PgFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
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
            IsAvailable = true;
        }
        catch (NpgsqlException ex)
        {
            IsAvailable = false;
            SkipReason = $"PostgreSQL unavailable: {ex.Message}";
        }
        catch (SocketException ex)
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
        var options = new DbContextOptionsBuilder<CampusDbContext>()
            .UseNpgsql(ConnectionString).Options;
        await using var db = new CampusDbContext(options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
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
            Assert.Fail(fx.SkipReason ?? "PostgreSQL unavailable");
    }
}