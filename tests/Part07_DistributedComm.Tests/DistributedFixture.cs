using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Part07_DistributedComm.Tests;

public sealed class DistributedFixture : IAsyncLifetime
{
    public const string SigningKey = "w7-test-signing-key-that-is-at-least-32-bytes";
    public const string InternalToken =
        "w7-test-gateway-token-that-is-at-least-32-bytes";
    public const string Issuer = "campus-gateway";
    public const string Audience = "campus-capstone";

    private readonly List<Process> _processes = [];
    private readonly ConcurrentQueue<string> _logs = [];
    private PostgreSqlContainer? _postgresContainer;
    private IContainer? _rabbitContainer;
    private string _rabbitMq = "";

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public int CatalogHttpPort { get; private set; }

    public int CatalogGrpcPort { get; private set; }

    public int EnrollmentPort { get; private set; }

    public int NoticesPort { get; private set; }

    public int GatewayPort { get; private set; }

    public string CatalogUrl => $"http://127.0.0.1:{CatalogHttpPort}/";

    public string CatalogGrpcUrl => $"http://127.0.0.1:{CatalogGrpcPort}/";

    public string EnrollmentUrl => $"http://127.0.0.1:{EnrollmentPort}/";

    public string NoticesUrl => $"http://127.0.0.1:{NoticesPort}/";

    public string GatewayUrl => $"http://127.0.0.1:{GatewayPort}/";

    public string CatalogDatabase { get; private set; } = "";

    public string EnrollmentDatabase { get; private set; } = "";

    public string NoticesDatabase { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        if (!await ConfigureInfrastructureAsync())
        {
            return;
        }

        CatalogHttpPort = FreeTcpPort();
        CatalogGrpcPort = FreeTcpPort();
        EnrollmentPort = FreeTcpPort();
        NoticesPort = FreeTcpPort();
        GatewayPort = FreeTcpPort();

        StartRole("Catalog", CatalogHttpPort, CatalogGrpcPort);
        await WaitUntilReadyAsync($"{CatalogUrl}health/ready");
        StartRole("Notices", NoticesPort);
        await WaitUntilReadyAsync($"{NoticesUrl}health/ready");
        StartRole("Enrollment", EnrollmentPort);
        await WaitUntilReadyAsync($"{EnrollmentUrl}health/ready");
        StartRole("Gateway", GatewayPort);
        await WaitUntilReadyAsync($"{GatewayUrl}health/ready");
        IsAvailable = true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var process in _processes)
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process already exited.
                }
            }
        }

        if (_rabbitContainer is not null)
        {
            await _rabbitContainer.DisposeAsync();
        }

        if (_postgresContainer is not null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }

    public HttpClient CreateGatewayClient() => new()
    {
        BaseAddress = new Uri(GatewayUrl),
        Timeout = TimeSpan.FromSeconds(10),
    };

    public async Task ResetAsync()
    {
        using var notices = new HttpClient { BaseAddress = new Uri(NoticesUrl) };
        using var purge = await notices.PostAsync("internal/rabbit/purge", null);
        purge.EnsureSuccessStatusCode();
        using var resetFaults = await notices.PostAsync(
            "internal/fault/configure/0",
            null);
        resetFaults.EnsureSuccessStatusCode();

        await using var connection = new NpgsqlConnection(EnrollmentDatabase);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE TABLE distributed_outbox, distributed_enrollments";
        await command.ExecuteNonQueryAsync();
    }

    public string Diagnostics() => string.Join(Environment.NewLine, _logs);

    private async Task<bool> ConfigureInfrastructureAsync()
    {
        var configuredPostgres = Environment.GetEnvironmentVariable("CAMPUS_W7_PG");
        if (!string.IsNullOrWhiteSpace(configuredPostgres))
        {
            CatalogDatabase = WithDatabase(configuredPostgres, "campus_w7_catalog_test");
            EnrollmentDatabase = WithDatabase(configuredPostgres, "campus_w7_enrollment_test");
            NoticesDatabase = WithDatabase(configuredPostgres, "campus_w7_notices_test");
            await EnsureDatabaseExistsAsync(CatalogDatabase);
            await EnsureDatabaseExistsAsync(EnrollmentDatabase);
            await EnsureDatabaseExistsAsync(NoticesDatabase);
        }
        else
        {
            try
            {
                _postgresContainer = new PostgreSqlBuilder("postgres:18.4-alpine")
                    .WithDatabase("postgres")
                    .WithUsername("dotnet")
                    .WithPassword("dotnet_dev")
                    .Build();
                await _postgresContainer.StartAsync();
                var baseConnection = _postgresContainer.GetConnectionString();
                CatalogDatabase = WithDatabase(baseConnection, "campus_w7_catalog_test");
                EnrollmentDatabase = WithDatabase(baseConnection, "campus_w7_enrollment_test");
                NoticesDatabase = WithDatabase(baseConnection, "campus_w7_notices_test");
                await EnsureDatabaseExistsAsync(CatalogDatabase);
                await EnsureDatabaseExistsAsync(EnrollmentDatabase);
                await EnsureDatabaseExistsAsync(NoticesDatabase);
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

        var configuredRabbit = Environment.GetEnvironmentVariable("CAMPUS_W7_RABBITMQ");
        if (!string.IsNullOrWhiteSpace(configuredRabbit))
        {
            _rabbitMq = configuredRabbit;
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
            _rabbitMq =
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

    private void StartRole(string role, int httpPort, int? grpcPort = null)
    {
        var assembly = typeof(CatalogStore).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assembly)
                ?? throw new InvalidOperationException("Part07 assembly has no directory."),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(assembly);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["Distributed__Role"] = role;
        start.Environment["Distributed__CatalogHttpUrl"] = CatalogUrl;
        start.Environment["Distributed__CatalogGrpcUrl"] = CatalogGrpcUrl;
        start.Environment["Distributed__EnrollmentUrl"] = EnrollmentUrl;
        start.Environment["Distributed__NoticesUrl"] = NoticesUrl;
        start.Environment["ConnectionStrings__Catalog"] = CatalogDatabase;
        start.Environment["ConnectionStrings__Enrollment"] = EnrollmentDatabase;
        start.Environment["ConnectionStrings__Messaging"] = NoticesDatabase;
        start.Environment["ConnectionStrings__RabbitMQ"] = _rabbitMq;
        start.Environment["GatewayAuth__Issuer"] = Issuer;
        start.Environment["GatewayAuth__Audience"] = Audience;
        start.Environment["GatewayAuth__SigningKey"] = SigningKey;
        start.Environment["GatewayAuth__InternalToken"] = InternalToken;
        start.Environment["RabbitMQ__RetryDelayMilliseconds"] = "50";
        start.Environment["RabbitMQ__MaxDeliveryAttempts"] = "3";
        start.Environment["RabbitMQ__Prefetch"] = "4";
        start.Environment["Resilience__RetryAttempts"] = "1";
        start.Environment["Resilience__RetryDelayMs"] = "25";
        start.Environment["Resilience__AttemptTimeoutMs"] = "300";
        start.Environment["Resilience__TotalTimeoutMs"] = "1200";
        start.Environment["Resilience__CircuitMinimumThroughput"] = "2";
        start.Environment["Resilience__CircuitSamplingMs"] = "2000";
        start.Environment["Resilience__CircuitBreakMs"] = "1000";
        start.Environment["Kestrel__Endpoints__Http__Url"] =
            $"http://127.0.0.1:{httpPort}";
        start.Environment["Kestrel__Endpoints__Http__Protocols"] = "Http1";
        if (grpcPort is not null)
        {
            start.Environment["Kestrel__Endpoints__Grpc__Url"] =
                $"http://127.0.0.1:{grpcPort}";
            start.Environment["Kestrel__Endpoints__Grpc__Protocols"] = "Http2";
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => Capture(role, args.Data);
        process.ErrorDataReceived += (_, args) => Capture(role, args.Data);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start {role}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _processes.Add(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private void Capture(string role, string? message)
    {
        if (message is null)
        {
            return;
        }

        _logs.Enqueue($"[{role}] {message}");
        while (_logs.Count > 1000)
        {
            _logs.TryDequeue(out _);
        }
    }

    private async Task WaitUntilReadyAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var timeout = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < timeout)
        {
            try
            {
                using var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Process has not bound the port yet.
            }
            catch (TaskCanceledException)
            {
                // Readiness can time out while a dependency is starting.
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Timed out waiting for {url}.");
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string WithDatabase(string connectionString, string database)
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
        var database = builder.Database
            ?? throw new InvalidOperationException("PostgreSQL database name is required.");
        builder.Database = "postgres";
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
            exists.Parameters.AddWithValue("name", database);
            if (await exists.ExecuteScalarAsync() is not null)
            {
                return;
            }
        }

        await using var create = connection.CreateCommand();
        create.CommandText =
            $"CREATE DATABASE \"{database.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        await create.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DistributedCollection : ICollectionFixture<DistributedFixture>
{
    public const string Name = "w7-distributed";
}
