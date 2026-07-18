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
using Part04_3_MultiTenant;

namespace Part04_3_MultiTenant.Tests;

public sealed class TenantFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("CAMPUS_TENANT_TEST_PG")
                           ?? Environment.GetEnvironmentVariable("CAMPUS_TEST_PG")
                           ?? "Host=localhost;Port=5432;Database=campus_tenant_test;Username=dotnet;Password=dotnet_dev";
        try
        {
            await EnsureDatabaseExistsAsync(ConnectionString);
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            var options = new DbContextOptionsBuilder<TenantDbContext>()
                .UseNpgsql(ConnectionString).Options;
            // Need a dummy ITenantContext for migration
            var dummyTenant = new DummyTenant();
            await using var db = new TenantDbContext(options, dummyTenant);
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
            SkipReason = $"PostgreSQL unavailable: {ex.Message}";
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            IsAvailable = false;
            SkipReason = $"Socket: {ex.Message}";
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public WebApplicationFactory<Program> CreateFactory(string? tenantId = null)
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

    private sealed class DummyTenant : ITenantContext
    {
        public string? CurrentCollegeId => "college-1";
    }
}

[CollectionDefinition("tenant")]
public sealed class TenantCollection : ICollectionFixture<TenantFixture> { }

public static class TenantSkip
{
    public static void IfNotAvailable(TenantFixture fx)
    {
        if (!fx.IsAvailable)
            global::Xunit.Skip.If(fx.SkipReason is not null, fx.SkipReason ?? "PostgreSQL unavailable");
    }
}
