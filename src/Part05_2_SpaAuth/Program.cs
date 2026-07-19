// LearnAspNet
// Doc   : ASP.NetStudy/第5部分-2-前后端分离SPA认证-完整实施指南.md
// Part  : Part05_2 · SPA/BFF
// Focus : Same-origin SPA, confidential OIDC code+PKCE client, opaque HttpOnly session
//         cookie, Redis token storage, CSRF enforcement, refresh/revocation and YARP.

using System.Security.Claims;
using Campus.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Part05_2_SpaAuth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddCampusBff(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; " +
        "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    await next(context);
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/bff/api"),
    branch =>
    {
        branch.UseMiddleware<BffCsrfMiddleware>();
        branch.UseMiddleware<BffAccessTokenMiddleware>();
    });

app.UseAuthorization();

app.MapGet("/bff/login", (string? returnUrl) =>
{
    var safeReturnUrl = LocalReturnUrl(returnUrl) ? returnUrl! : "/";
    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = safeReturnUrl },
        [BffSchemes.Oidc]);
}).AllowAnonymous();

app.MapGet("/bff/user", async (HttpContext context) =>
{
    var authentication = await context.AuthenticateAsync(BffSchemes.Cookie);
    if (!authentication.Succeeded || authentication.Principal is null)
    {
        return Results.Ok(new { isAuthenticated = false });
    }

    var user = authentication.Principal;
    return Results.Ok(new
    {
        isAuthenticated = true,
        subject = user.FindFirstValue("sub"),
        name = user.Identity?.Name,
        claims = user.Claims
            .Where(claim => claim.Type is "name" or "preferred_username" or "email")
            .Select(claim => new { claim.Type, claim.Value })
            .Distinct(),
        // Tokens are intentionally never returned to JavaScript.
    });
}).AllowAnonymous();

app.MapPost("/bff/logout", async (
    HttpContext context,
    BffAccessTokenService accessTokens,
    IOptions<BffOptions> options,
    CancellationToken cancellationToken) =>
{
    var csrfError = BffCsrfProtection.Validate(context.Request, options.Value);
    if (csrfError is not null)
    {
        return Results.Problem(
            statusCode: csrfError == "cross_origin_request_rejected" ? 403 : 400,
            title: "The BFF logout request failed CSRF validation.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = csrfError });
    }

    await accessTokens.RevokeCurrentSessionAsync(context, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization("BffUser");

app.MapPost("/bff/backchannel-logout", async (
    HttpContext context,
    SessionRevocationStore revocations,
    CancellationToken cancellationToken) =>
{
    var authentication = await context.AuthenticateAsync(BffSchemes.BackchannelLogout);
    if (!authentication.Succeeded ||
        authentication.Principal is null ||
        !BackchannelLogoutTokenValidator.IsValid(authentication.Principal))
    {
        return Results.BadRequest(new { errorCode = "invalid_logout_token" });
    }

    var sid = authentication.Principal.FindFirstValue("sid");
    var sub = authentication.Principal.FindFirstValue("sub");
    if (string.IsNullOrWhiteSpace(sid) && string.IsNullOrWhiteSpace(sub))
    {
        return Results.BadRequest(new { errorCode = "logout_token_missing_session" });
    }

    await revocations.RevokeAsync(
        sid,
        sub,
        DateTimeOffset.UtcNow.AddHours(8),
        cancellationToken);
    return Results.NoContent();
}).AllowAnonymous();

app.MapReverseProxy();
app.MapFallbackToFile("index.html");

app.Run();

static bool LocalReturnUrl(string? returnUrl) =>
    !string.IsNullOrWhiteSpace(returnUrl) &&
    returnUrl[0] == '/' &&
    (returnUrl.Length == 1 || returnUrl[1] is not '/' and not '\\');

public partial class Program;
