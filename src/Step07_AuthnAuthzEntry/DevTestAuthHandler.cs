using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Step07_AuthnAuthzEntry;

/// <summary>Lab/test auth via X-Test-* headers (mirrors Campus.Testing.TestAuthHandler).</summary>
public sealed class DevTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public DevTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userHeader) ||
            string.IsNullOrWhiteSpace(userHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var sub = userHeader.ToString();
        var role = Request.Headers.TryGetValue("X-Test-Role", out var roleHeader)
            ? roleHeader.ToString()
            : "Student";
        var collegeId = Request.Headers.TryGetValue("X-Test-College", out var collegeHeader)
            ? collegeHeader.ToString()
            : "college-1";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, sub),
            new("sub", sub),
            new(ClaimTypes.Role, role),
            new("role", role),
            new("college_id", collegeId),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
