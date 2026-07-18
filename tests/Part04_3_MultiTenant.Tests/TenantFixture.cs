using System.Net.Sockets;
using Campus.Testing;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Part04_3_MultiTenant;
using Testcontainers.PostgreSql;

namespace Part04_3_MultiTenant.Tests;

public sealed class TenantFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = "";
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        // 1) Try Testcontainers
        try
        {
            _container = new PostgreSqlBuilder("postgres:18.4-alpine")
                .WithDatabase("campus_tenant_test")
                .WithUsername("dotnet")
                .WithPassword("dotnet_dev")
                .Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (DockerUnavailableException ex) { SkipReason = $"Docker: {ex.Message}"; _container = null; }
        catch (DockerImageNotFoundException ex) { SkipReason = $"Image: {ex.Message}"; _container = null; }
        catch (DockerApiException ex) { SkipReason = $"Docker API: {ex.Message}"; _container = null; }
        catch (TimeoutException ex) { SkipReason = $"Timeout: {ex.Message}"; _container = null; }
        catch (IOException ex) { SkipReason = $"IO: {ex.Message}"; _container = null; }
        catch (SocketException ex) { SkipReason = $"Socket: {ex.Message}"; _container = null; }
        catch (HttpRequestException ex) { SkipReason = $"HTTP: {ex.Message}"; _container = null; }

        // 2) Fallback: local PG
        if (_container is null)
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
                SkipReason = null;
            }
            catch (NpgsqlException ex) { IsAvailable = false; SkipReason = $"PG: {ex.Message}"; return; }
            catch (SocketException ex) { IsAvailable = false; SkipReason = $"Socket: {ex.Message}"; return; }
        }

        // 3) Migrate
        try
        {
            var options = new DbContextOptionsBuilder<TenantDbContext>()
                .UseNpgsql(ConnectionString).Options;
            var dummyTenant = new DummyTenant();
            await using var db = new TenantDbContext(options, dummyTenant);
            try { await db.Database.MigrateAsync(); }
            catch (NpgsqlException) { await db.Database.EnsureDeletedAsync(); await db.Database.MigrateAsync(); }
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
        if (_container is not null) await _container.DisposeAsync();
    }

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
