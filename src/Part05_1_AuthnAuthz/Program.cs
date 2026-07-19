// LearnAspNet
// Doc   : ASP.NetStudy/第5部分-1-认证授权核心-完整实施指南.md
// Part  : Part05_1 · Authn/Authz
// Focus : Keycloak OIDC/JWKS, Authorization Code + PKCE, policy/resource authorization,
//         explicit CORS, partitioned rate limiting, Redis sessions and Data Protection keys.

using System.Security.Claims;
using Campus.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Part05_1_AuthnAuthz;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddW6Security(builder.Configuration);
builder.Services.AddSingleton<CourseResourceStore>();
builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy());

var app = builder.Build();

app.UseExceptionHandler();
app.UseRouting();

// Preflight requests don't carry bearer credentials. CORS must run before authentication.
app.UseCors("CampusSpa");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/", (IOptions<W6SecurityOptions> options) => Results.Ok(new
{
    lab = "Part05_1_AuthnAuthz",
    identityProvider = options.Value.Authority,
    audience = options.Value.Audience,
    tokenValidation = new[]
    {
        "issuer",
        "audience",
        "lifetime",
        "signing-key",
        "clock-skew",
    },
    secretsIncluded = false,
}));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Name == "self",
});

app.MapGet("/auth/login", (string? returnUrl) =>
{
    var safeReturnUrl = LocalReturnUrl(returnUrl) ? returnUrl! : "/auth/me";
    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = safeReturnUrl },
        [SecuritySchemes.WebOidc]);
}).RequireRateLimiting("login");

app.MapGet("/auth/me", (ClaimsPrincipal user) => Results.Ok(new
{
    subject = user.FindFirstValue("sub"),
    name = user.Identity?.Name,
    roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct().Order(),
    // Deliberately no access, ID, or refresh token in this response.
})).RequireAuthorization("WebUser");

app.MapPost("/auth/logout", () => Results.SignOut(
    new AuthenticationProperties { RedirectUri = "/" },
    [SecuritySchemes.WebCookie, SecuritySchemes.WebOidc]))
    .RequireAuthorization("WebUser");

app.MapPost("/backchannel-logout", async (
    HttpContext httpContext,
    SessionRevocationStore revocations,
    CancellationToken cancellationToken) =>
{
    var result = await httpContext.AuthenticateAsync(SecuritySchemes.BackchannelLogout);
    if (!result.Succeeded ||
        result.Principal is null ||
        !BackchannelLogoutTokenValidator.IsValid(result.Principal))
    {
        return Results.BadRequest(new { errorCode = "invalid_logout_token" });
    }

    var sid = result.Principal.FindFirstValue("sid");
    var sub = result.Principal.FindFirstValue("sub");
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

var api = app.MapGroup("/api")
    .RequireAuthorization()
    .RequireRateLimiting("api");

api.MapGet("/identity", (ClaimsPrincipal user) => Results.Ok(new
{
    subject = Subject(user),
    name = user.Identity?.Name,
    scopes = user.FindAll("scope")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Distinct()
        .Order(),
    roles = user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct().Order(),
})).RequireAuthorization("CampusRead");

api.MapGet("/courses", (ClaimsPrincipal user, CourseResourceStore store) =>
{
    var subject = Subject(user);
    return Results.Ok(store.ListFor(subject, user.IsInRole("Admin")));
}).RequireAuthorization("CampusRead");

api.MapGet("/courses/{id:guid}", async (
    Guid id,
    ClaimsPrincipal user,
    CourseResourceStore store,
    IAuthorizationService authorization) =>
{
    var course = store.Find(id);
    if (course is null)
    {
        return Results.NotFound();
    }

    var decision = await authorization.AuthorizeAsync(user, course, new SameOwnerRequirement());
    return decision.Succeeded ? Results.Ok(course) : Results.Forbid();
}).RequireAuthorization("CampusRead");

api.MapPost("/courses", (CreateCourseRequest request, ClaimsPrincipal user, CourseResourceStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Code) ||
        request.Code.Length > 32 ||
        string.IsNullOrWhiteSpace(request.Title) ||
        request.Title.Length > 200)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["course"] = ["Code (1-32) and title (1-200) are required."],
        });
    }

    var course = store.Create(request.Code, request.Title, Subject(user));
    return Results.Created($"/api/courses/{course.Id}", course);
}).RequireAuthorization("CampusWrite");

api.MapPut("/courses/{id:guid}", async (
    Guid id,
    UpdateCourseRequest request,
    ClaimsPrincipal user,
    CourseResourceStore store,
    IAuthorizationService authorization) =>
{
    var course = store.Find(id);
    if (course is null)
    {
        return Results.NotFound();
    }

    var decision = await authorization.AuthorizeAsync(user, course, new SameOwnerRequirement());
    if (!decision.Succeeded)
    {
        return Results.Forbid();
    }

    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = ["Title (1-200) is required."],
        });
    }

    return Results.Ok(store.Update(id, request.Title));
}).RequireAuthorization("CampusWrite");

api.MapDelete("/courses/{id:guid}", async (
    Guid id,
    ClaimsPrincipal user,
    CourseResourceStore store,
    IAuthorizationService authorization) =>
{
    var course = store.Find(id);
    if (course is null)
    {
        return Results.NotFound();
    }

    var decision = await authorization.AuthorizeAsync(user, course, new SameOwnerRequirement());
    if (!decision.Succeeded)
    {
        return Results.Forbid();
    }

    return store.Remove(id) ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization("CampusWrite");

api.MapGet("/admin/audit", () => Results.Ok(new
{
    eventName = "security-audit",
    generatedAt = DateTimeOffset.UtcNow,
})).RequireAuthorization("AdminOnly");

app.Run();

static string Subject(ClaimsPrincipal user) =>
    user.FindFirstValue("sub") ??
    user.FindFirstValue(ClaimTypes.NameIdentifier) ??
    throw new InvalidOperationException("The authenticated principal has no stable subject.");

static bool LocalReturnUrl(string? returnUrl) =>
    !string.IsNullOrWhiteSpace(returnUrl) &&
    returnUrl[0] == '/' &&
    (returnUrl.Length == 1 || returnUrl[1] is not '/' and not '\\');

public partial class Program;
