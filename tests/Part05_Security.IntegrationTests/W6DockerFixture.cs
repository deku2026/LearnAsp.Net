using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Part05_Security.IntegrationTests;

public sealed class W6DockerFixture : IAsyncLifetime
{
    private readonly List<Process> _applications = [];
    private readonly ConcurrentQueue<string> _applicationLogs = [];
    private IContainer? _keycloakContainer;
    private IContainer? _redisContainer;
    private string _adminUsername = "admin";
    private string _adminPassword = "admin";

    public string RealmName { get; } = $"campus-w6-{Guid.NewGuid():N}";

    public string TestPassword { get; } =
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    public string WebClientSecret { get; } =
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public string BffClientSecret { get; } =
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public string KeycloakBaseUrl { get; private set; } = "";

    public string Authority => $"{KeycloakBaseUrl}/realms/{RealmName}";

    public string RedisConnectionString { get; private set; } = "";

    public int ApiPort { get; private set; }

    public int WebPort { get; private set; }

    public int BffPort { get; private set; }

    public int BffSecondPort { get; private set; }

    public string ApiBaseUrl => $"http://127.0.0.1:{ApiPort}";

    public string WebBaseUrl => ApiBaseUrl;

    public string BffBaseUrl => $"http://127.0.0.1:{BffPort}";

    public string BffSecondBaseUrl => $"http://127.0.0.1:{BffSecondPort}";

    public async ValueTask InitializeAsync()
    {
        await EnsureKeycloakAsync();
        await EnsureRedisAsync();
        ApiPort = FreeTcpPort();
        WebPort = ApiPort;
        BffPort = FreeTcpPort();
        BffSecondPort = FreeTcpPort();

        await CreateRealmAsync();
        StartApplication(
            "Part05_1_AuthnAuthz",
            ApiPort,
            new Dictionary<string, string?>
            {
                ["Security__Authority"] = Authority,
                ["Security__Audience"] = "campus-api",
                ["Security__WebClientId"] = "campus-web",
                ["Security__WebClientSecret"] = WebClientSecret,
                ["Security__RequireHttpsMetadata"] = "false",
                ["Security__RequireSecureCookies"] = "false",
                ["Security__UseRedis"] = "true",
                ["Security__ClockSkewSeconds"] = "0",
                ["ConnectionStrings__Redis"] = RedisConnectionString,
            });
        StartApplication(
            "Part05_2_SpaAuth",
            BffPort,
            BffEnvironment(BffPort));

        await WaitUntilReadyAsync($"{ApiBaseUrl}/");
        await WaitUntilReadyAsync($"{BffBaseUrl}/bff/user");

        // Start the second instance after the first has initialized the shared key
        // ring, matching a rolling deployment rather than racing two empty rings.
        StartApplication(
            "Part05_2_SpaAuth",
            BffSecondPort,
            BffEnvironment(BffSecondPort));
        await WaitUntilReadyAsync($"{BffSecondBaseUrl}/bff/user");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var process in _applications)
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
                // The process already exited.
            }
            finally
            {
                process.Dispose();
            }
        }

        try
        {
            await DeleteRealmAsync();
        }
        catch (HttpRequestException)
        {
            // A fixture-owned Keycloak container may already be stopping.
        }

        if (_keycloakContainer is not null)
        {
            await _keycloakContainer.DisposeAsync();
        }

        if (_redisContainer is not null)
        {
            await _redisContainer.DisposeAsync();
        }
    }

    public async Task<string> GetAccessTokenAsync(
        string username,
        string clientId = "campus-spa")
    {
        using var client = new HttpClient();
        using var response = await client.PostAsync(
            $"{Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = clientId,
                ["username"] = username,
                ["password"] = TestPassword,
                ["scope"] = "openid campus.read campus.write",
            }));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak returned no access token.");
    }

    public string Diagnostics() => string.Join(Environment.NewLine, _applicationLogs);

    private Dictionary<string, string?> BffEnvironment(int port) => new()
    {
        ["Bff__Authority"] = Authority,
        ["Bff__ClientId"] = "campus-bff",
        ["Bff__ClientSecret"] = BffClientSecret,
        ["Bff__DownstreamApi"] = ApiBaseUrl,
        ["Bff__PublicOrigin"] = $"http://127.0.0.1:{port}",
        ["Bff__RequireHttpsMetadata"] = "false",
        ["Bff__RequireSecureCookies"] = "false",
        ["Bff__UseRedis"] = "true",
        ["Bff__RefreshBeforeExpirySeconds"] = "3",
        ["ConnectionStrings__Redis"] = RedisConnectionString,
    };

    private async Task EnsureKeycloakAsync()
    {
        _adminUsername = Environment.GetEnvironmentVariable("CAMPUS_KEYCLOAK_ADMIN") ?? "admin";
        _adminPassword = Environment.GetEnvironmentVariable("CAMPUS_KEYCLOAK_ADMIN_PASSWORD") ?? "admin";
        var configured = Environment.GetEnvironmentVariable("CAMPUS_KEYCLOAK_URL")
            ?? "http://127.0.0.1:8082";
        if (await IsHttpReadyAsync(
                $"{configured.TrimEnd('/')}/realms/master/.well-known/openid-configuration"))
        {
            KeycloakBaseUrl = configured.TrimEnd('/');
            return;
        }

        _keycloakContainer = new ContainerBuilder("quay.io/keycloak/keycloak:26.7.0")
            .WithCommand("start-dev")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", _adminUsername)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", _adminPassword)
            .WithEnvironment("KC_HTTP_PORT", "8080")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                request.ForPort(8080)
                    .ForPath("/realms/master/.well-known/openid-configuration")))
            .Build();
        await _keycloakContainer.StartAsync();
        KeycloakBaseUrl =
            $"http://127.0.0.1:{_keycloakContainer.GetMappedPublicPort(8080)}";
    }

    private async Task EnsureRedisAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CAMPUS_REDIS") ??
                         "127.0.0.1:6380,abortConnect=false";
        var endpoint = configured.Split(',')[0].Split(':');
        if (endpoint.Length == 2 &&
            int.TryParse(endpoint[1], out var port) &&
            await IsTcpReadyAsync(endpoint[0], port))
        {
            RedisConnectionString = configured;
            return;
        }

        _redisContainer = new ContainerBuilder("redis:8.8.0-alpine")
            .WithCommand("redis-server", "--appendonly", "no")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
            .Build();
        await _redisContainer.StartAsync();
        RedisConnectionString =
            $"127.0.0.1:{_redisContainer.GetMappedPublicPort(6379)},abortConnect=false";
    }

    private async Task CreateRealmAsync()
    {
        var token = await GetAdminTokenAsync();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsJsonAsync(
            $"{KeycloakBaseUrl}/admin/realms",
            RealmRepresentation());
        if (response.StatusCode != HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Keycloak realm creation failed ({(int)response.StatusCode}): {body}");
        }

        await CaptureClientScopesAsync(client, "campus-web");
        await CaptureClientScopesAsync(client, "campus-bff");
        await WaitUntilReadyAsync(
            $"{Authority}/.well-known/openid-configuration");
    }

    private async Task CaptureClientScopesAsync(HttpClient client, string clientId)
    {
        var clients = await client.GetFromJsonAsync<JsonElement>(
            $"{KeycloakBaseUrl}/admin/realms/{RealmName}/clients?clientId={clientId}");
        var internalId = clients.EnumerateArray().Single().GetProperty("id").GetString();
        var defaultScopes = await client.GetFromJsonAsync<JsonElement>(
            $"{KeycloakBaseUrl}/admin/realms/{RealmName}/clients/{internalId}/default-client-scopes");
        var optionalScopes = await client.GetFromJsonAsync<JsonElement>(
            $"{KeycloakBaseUrl}/admin/realms/{RealmName}/clients/{internalId}/optional-client-scopes");
        var defaults = defaultScopes.EnumerateArray()
            .Select(scope => scope.GetProperty("name").GetString());
        var optional = optionalScopes.EnumerateArray()
            .Select(scope => scope.GetProperty("name").GetString());
        _applicationLogs.Enqueue(
            $"[Keycloak] {clientId} default=[{string.Join(',', defaults)}] " +
            $"optional=[{string.Join(',', optional)}]");
    }

    private async Task DeleteRealmAsync()
    {
        if (string.IsNullOrWhiteSpace(KeycloakBaseUrl))
        {
            return;
        }

        var token = await GetAdminTokenAsync();
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await client.DeleteAsync($"{KeycloakBaseUrl}/admin/realms/{RealmName}");
    }

    private async Task<string> GetAdminTokenAsync()
    {
        using var client = new HttpClient();
        using var response = await client.PostAsync(
            $"{KeycloakBaseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = _adminUsername,
                ["password"] = _adminPassword,
            }));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak returned no admin token.");
    }

    private JsonElement RealmRepresentation()
    {
        var templatePath = Path.Combine(
            FindRepositoryRoot(),
            "deploy",
            "keycloak",
            "campus-w6-realm.template.json");
        var json = File.ReadAllText(templatePath);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__REALM__"] = RealmName,
            ["__PASSWORD__"] = TestPassword,
            ["__WEB_SECRET__"] = WebClientSecret,
            ["__BFF_SECRET__"] = BffClientSecret,
            ["__WEB_BASE_URL__"] = WebBaseUrl,
            ["__BFF_BASE_URL__"] = BffBaseUrl,
            ["__BFF_SECOND_BASE_URL__"] = BffSecondBaseUrl,
            ["__BFF_ACCESS_TOKEN_LIFESPAN__"] = "8",
        };
        foreach (var replacement in replacements)
        {
            var encodedValue = JsonSerializer.Serialize(replacement.Value);
            json = json.Replace(
                replacement.Key,
                encodedValue[1..^1],
                StringComparison.Ordinal);
        }

        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private void StartApplication(
        string projectName,
        int port,
        IReadOnlyDictionary<string, string?> environment)
    {
        var repository = FindRepositoryRoot();
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var assembly = Path.Combine(
            repository,
            "src",
            projectName,
            "bin",
            configuration,
            "net10.0",
            $"{projectName}.dll");
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException(
                $"Build output for {projectName} was not found.",
                assembly);
        }

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["DOTNET_NOLOGO"] = "1";
        foreach (var pair in environment)
        {
            if (pair.Value is not null)
            {
                start.Environment[pair.Key] = pair.Value;
            }
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => Capture(projectName, port, args.Data);
        process.ErrorDataReceived += (_, args) => Capture(projectName, port, args.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {projectName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _applications.Add(process);
    }

    private void Capture(string project, int port, string? message)
    {
        if (message is not null)
        {
            _applicationLogs.Enqueue($"[{project}:{port}] {message}");
            while (_applicationLogs.Count > 500)
            {
                _applicationLogs.TryDequeue(out _);
            }
        }
    }

    private async Task WaitUntilReadyAsync(string url)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < timeout)
        {
            if (await IsHttpReadyAsync(url))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Timed out waiting for {url}.{Environment.NewLine}{Diagnostics()}");
    }

    private static async Task<bool> IsHttpReadyAsync(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> IsTcpReadyAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(2));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LearnAspNet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the LearnAspNet repository root.");
    }
}

[CollectionDefinition("w6-docker", DisableParallelization = true)]
public sealed class W6DockerCollection : ICollectionFixture<W6DockerFixture>;
