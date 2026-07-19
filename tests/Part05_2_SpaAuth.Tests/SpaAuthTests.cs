using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Campus.Security;
using Campus.Testing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Part05_2_SpaAuth.Tests;

public sealed class SpaAuthFixture : IDisposable
{
    public CampusWebApplicationFactory<Program> Factory { get; } =
        new CampusWebApplicationFactory<Program>()
            .WithSetting("Bff:UseRedis", "false")
            .WithSetting("Bff:Authority", "https://issuer.invalid/realms/test")
            .WithSetting("Bff:ClientSecret", "test-only")
            .WithSetting("Bff:DownstreamApi", "http://127.0.0.1:1");

    public void Dispose() => Factory.Dispose();
}

public sealed class SpaAuthTests(SpaAuthFixture fixture) : IClassFixture<SpaAuthFixture>
{
    [Fact]
    public async Task Spa_is_same_origin_and_has_strict_browser_headers()
    {
        using var response = await fixture.Factory.CreateClient().GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("浏览器里没有 token", html, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Anonymous_user_endpoint_exposes_no_tokens()
    {
        var response = await fixture.Factory.CreateClient().GetFromJsonAsync<JsonElement>("/bff/user");

        Assert.False(response.GetProperty("isAuthenticated").GetBoolean());
        Assert.DoesNotContain("token", response.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bff_api_requires_csrf_header_before_session()
    {
        var client = fixture.Factory.CreateClient();

        using var missingHeader = await client.GetAsync("/bff/api/courses");
        Assert.Equal(HttpStatusCode.BadRequest, missingHeader.StatusCode);
        var missingProblem = await missingHeader.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("csrf_header_missing", missingProblem.GetProperty("errorCode").GetString());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/api/courses");
        request.Headers.Add(BffCsrfProtection.HeaderName, BffCsrfProtection.HeaderValue);
        using var missingSession = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, missingSession.StatusCode);
    }

    [Fact]
    public async Task Cross_origin_preflight_never_receives_cors_permission()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/bff/api/courses");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-csrf,content-type");

        using var response = await fixture.Factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void Cross_origin_request_is_rejected_even_with_csrf_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("campus.example");
        context.Request.Headers[BffCsrfProtection.HeaderName] = BffCsrfProtection.HeaderValue;
        context.Request.Headers.Origin = "https://evil.example";

        var result = BffCsrfProtection.Validate(context.Request, new BffOptions());

        Assert.Equal("cross_origin_request_rejected", result);
    }

    [Fact]
    public void Authentication_ticket_is_server_side_and_cookie_is_http_only_lax()
    {
        var monitor = fixture.Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var cookie = monitor.Get(BffSchemes.Cookie);

        Assert.NotNull(cookie.SessionStore);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);
        Assert.True(cookie.SlidingExpiration);
    }

    [Fact]
    public async Task Backchannel_logout_with_sid_does_not_block_a_new_user_session()
    {
        var revocations = fixture.Factory.Services.GetRequiredService<SessionRevocationStore>();
        await revocations.RevokeAsync(
            "old-session",
            "alice",
            DateTimeOffset.UtcNow.AddMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.True(await revocations.IsRevokedAsync(
            "old-session",
            "alice",
            TestContext.Current.CancellationToken));
        Assert.False(await revocations.IsRevokedAsync(
            "new-session",
            "alice",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Backchannel_logout_requires_spec_event_jti_iat_and_no_nonce()
    {
        var valid = LogoutPrincipal(
            new("jti", "logout-1"),
            new("iat", "1735689600"),
            new("events", """
                {"http://schemas.openid.net/event/backchannel-logout":{}}
                """));
        var malformedEvents = LogoutPrincipal(
            new("jti", "logout-2"),
            new("iat", "1735689600"),
            new("events", "{"));
        var nonce = LogoutPrincipal(
            new("jti", "logout-3"),
            new("iat", "1735689600"),
            new("nonce", "not-allowed"),
            new("events", """
                {"http://schemas.openid.net/event/backchannel-logout":{}}
                """));

        Assert.True(BackchannelLogoutTokenValidator.IsValid(valid));
        Assert.False(BackchannelLogoutTokenValidator.IsValid(malformedEvents));
        Assert.False(BackchannelLogoutTokenValidator.IsValid(nonce));
        Assert.False(BackchannelLogoutTokenValidator.IsValid(new ClaimsPrincipal()));
    }

    private static ClaimsPrincipal LogoutPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "logout-token"));
}
