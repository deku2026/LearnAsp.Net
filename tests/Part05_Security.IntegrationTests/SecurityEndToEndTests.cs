using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StackExchange.Redis;

namespace Part05_Security.IntegrationTests;

[Collection("w6-docker")]
[Trait("Category", "Docker")]
public sealed class SecurityEndToEndTests(W6DockerFixture fixture)
{
    [Fact]
    public async Task Keycloak_discovery_and_real_jwks_validate_access_token()
    {
        var token = await fixture.GetAccessTokenAsync("alice");
        var tokenPayload = TokenPart(token, 1);
        Assert.True(tokenPayload.TryGetProperty("sub", out _), tokenPayload.ToString());
        using var client = ApiClient(token);

        using var response = await client.GetAsync("/api/identity");

        Assert.True(response.IsSuccessStatusCode, fixture.Diagnostics());
        var identity = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alice", identity.GetProperty("name").GetString());
        Assert.Contains(
            "campus.read",
            identity.GetProperty("scopes").EnumerateArray().Select(item => item.GetString()));

        using var metadataClient = new HttpClient();
        var discovery = await metadataClient.GetFromJsonAsync<JsonElement>(
            $"{fixture.Authority}/.well-known/openid-configuration");
        var jwks = await metadataClient.GetFromJsonAsync<JsonElement>(
            discovery.GetProperty("jwks_uri").GetString()!);
        var tokenKid = TokenPart(token, 0).GetProperty("kid").GetString();
        Assert.Contains(
            jwks.GetProperty("keys").EnumerateArray(),
            key => key.GetProperty("kid").GetString() == tokenKid);
    }

    [Fact]
    public async Task Keycloak_roles_scopes_and_resource_owner_policy_are_enforced()
    {
        using var alice = ApiClient(await fixture.GetAccessTokenAsync("alice"));
        using var create = await alice.PostAsJsonAsync(
            "/api/courses",
            new { code = "REAL-SEC", title = "Real Keycloak authorization" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var course = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = course.GetProperty("id").GetGuid();

        using var bob = ApiClient(await fixture.GetAccessTokenAsync("bob"));
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bob.GetAsync($"/api/courses/{id}")).StatusCode);

        using var admin = ApiClient(await fixture.GetAccessTokenAsync("admin-user"));
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync($"/api/courses/{id}")).StatusCode);

        using var wrongAudience = ApiClient(
            await fixture.GetAccessTokenAsync("alice", "wrong-audience"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await wrongAudience.GetAsync("/api/identity")).StatusCode);
    }

    [Fact]
    public async Task Oidc_web_client_completes_real_authorization_code_pkce_flow()
    {
        var login = await OidcBrowser.LoginAsync(
            fixture.WebBaseUrl,
            "/auth/login?returnUrl=/auth/me",
            "alice",
            fixture.TestPassword,
            fixture.Diagnostics);
        using var browser = login.Browser;

        var me = await browser.GetFromJsonAsync<JsonElement>("/auth/me");

        Assert.Equal("alice", me.GetProperty("name").GetString());
        Assert.DoesNotContain("token", me.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            login.AuthenticationSetCookies,
            value => value.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase) &&
                     value.Contains("SameSite=Lax", StringComparison.OrdinalIgnoreCase));
        await AssertRedisContainsAsync("Part05_1:ticket");
        await AssertRedisContainsAsync("Part05_1:data-protection-keys");
    }

    [Fact]
    public async Task Bff_keeps_tokens_server_side_proxies_api_and_shares_session_between_instances()
    {
        var login = await OidcBrowser.LoginAsync(
            fixture.BffBaseUrl,
            "/bff/login?returnUrl=/",
            "alice",
            fixture.TestPassword,
            fixture.Diagnostics);
        using var browser = login.Browser;

        var user = await browser.GetFromJsonAsync<JsonElement>("/bff/user");
        Assert.True(user.GetProperty("isAuthenticated").GetBoolean());
        Assert.DoesNotContain("token", user.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            login.AuthenticationSetCookies,
            value => value.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase) &&
                     value.Contains("SameSite=Lax", StringComparison.OrdinalIgnoreCase) &&
                     !value.Contains("eyJ", StringComparison.Ordinal));

        using var create = new HttpRequestMessage(HttpMethod.Post, "/bff/api/courses")
        {
            Content = JsonContent.Create(new
            {
                code = "BFF-101",
                title = "Token stays server-side",
            }),
        };
        create.Headers.Add("X-CSRF", "1");
        using var created = await browser.SendAsync(create);
        Assert.True(created.IsSuccessStatusCode, fixture.Diagnostics());

        // Cookies are scoped to the host, not the port. The second BFF instance can
        // decrypt the same opaque cookie and load its ticket from shared Redis.
        var sharedSession = await browser.GetFromJsonAsync<JsonElement>(
            $"{fixture.BffSecondBaseUrl}/bff/user");
        Assert.True(sharedSession.GetProperty("isAuthenticated").GetBoolean());

        using var proxied = new HttpRequestMessage(
            HttpMethod.Get,
            $"{fixture.BffSecondBaseUrl}/bff/api/courses");
        proxied.Headers.Add("X-CSRF", "1");
        using var proxiedResponse = await browser.SendAsync(proxied);
        Assert.True(proxiedResponse.IsSuccessStatusCode, fixture.Diagnostics());
        var courses = await proxiedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            courses.EnumerateArray(),
            course => course.GetProperty("code").GetString() == "BFF-101");

        await AssertRedisContainsAsync("Part05_2:ticket");
        await AssertRedisContainsAsync("Part05_2:data-protection-keys");
    }

    [Fact]
    public async Task Bff_rejects_csrf_and_refreshes_expired_access_token()
    {
        var login = await OidcBrowser.LoginAsync(
            fixture.BffBaseUrl,
            "/bff/login?returnUrl=/",
            "alice",
            fixture.TestPassword,
            fixture.Diagnostics);
        using var browser = login.Browser;

        using var missingHeader = await browser.GetAsync("/bff/api/courses");
        Assert.Equal(HttpStatusCode.BadRequest, missingHeader.StatusCode);

        using var crossOrigin = new HttpRequestMessage(HttpMethod.Get, "/bff/api/courses");
        crossOrigin.Headers.Add("X-CSRF", "1");
        crossOrigin.Headers.Add("Origin", "https://attacker.example");
        using var rejected = await browser.SendAsync(crossOrigin);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        // campus-bff access tokens live for eight seconds in the test realm.
        // A successful downstream request after that proves server-side refresh.
        await Task.Delay(TimeSpan.FromSeconds(10));
        using var afterExpiry = new HttpRequestMessage(HttpMethod.Get, "/bff/api/courses");
        afterExpiry.Headers.Add("X-CSRF", "1");
        using var refreshed = await browser.SendAsync(afterExpiry);
        Assert.True(refreshed.IsSuccessStatusCode, fixture.Diagnostics());
    }

    [Fact]
    public async Task Bff_logout_revokes_server_token_and_removes_session()
    {
        var login = await OidcBrowser.LoginAsync(
            fixture.BffBaseUrl,
            "/bff/login?returnUrl=/",
            "alice",
            fixture.TestPassword,
            fixture.Diagnostics);
        using var browser = login.Browser;
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logout.Headers.Add("X-CSRF", "1");

        using var response = await browser.SendAsync(logout);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = await browser.GetFromJsonAsync<JsonElement>("/bff/user");
        Assert.False(user.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Rate_limiter_returns_429_problem_and_retry_after_for_real_user_partition()
    {
        using var client = ApiClient(await fixture.GetAccessTokenAsync("rate-user"));
        HttpResponseMessage? rejected = null;
        for (var index = 0; index < 30; index++)
        {
            var response = await client.GetAsync("/api/identity");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }

            response.Dispose();
        }

        Assert.NotNull(rejected);
        using (rejected)
        {
            Assert.True(rejected.Headers.Contains("Retry-After"));
            Assert.Equal(
                "application/problem+json",
                rejected.Content.Headers.ContentType?.MediaType);
            var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("rate_limit_exceeded", problem.GetProperty("errorCode").GetString());
        }
    }

    private HttpClient ApiClient(string accessToken)
    {
        var client = new HttpClient { BaseAddress = new Uri(fixture.ApiBaseUrl) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task AssertRedisContainsAsync(string fragment)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(
            $"{fixture.RedisConnectionString},allowAdmin=true");
        var endpoint = redis.GetEndPoints().Single();
        var server = redis.GetServer(endpoint);
        var keys = server.Keys(pattern: $"*{fragment}*").Select(key => key.ToString()).ToArray();
        Assert.NotEmpty(keys);
    }

    private static JsonElement TokenPart(string token, int index)
    {
        var encoded = token.Split('.')[index]
            .Replace('-', '+')
            .Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(encoded)).RootElement.Clone();
    }
}
