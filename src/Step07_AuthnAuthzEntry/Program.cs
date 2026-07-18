// LearnAspNet
// Doc   : ASP.NetStudy/步骤7-认证授权接入点-完整实施指南.md
// Part  : Step07 · AuthnAuthzEntry
// Title : 认证授权接入点

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Campus.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Step07_AuthnAuthzEntry;

var builder = WebApplication.CreateBuilder(args);

var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["SigningKey"] ?? "campus-dev-signing-key-at-least-32-bytes!!";
var issuer = jwtSection["Issuer"] ?? "campus-dev";
var audience = jwtSection["Audience"] ?? "campus-api";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "smart";
        options.DefaultChallengeScheme = "smart";
    })
    .AddPolicyScheme("smart", "JWT or Test", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey("X-Test-User")
                ? DevTestAuthHandler.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = "sub",
        };
    })
    .AddScheme<AuthenticationSchemeOptions, DevTestAuthHandler>(DevTestAuthHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    options.AddPolicy("CanEnroll", p => p.RequireRole("Student", "Admin"));
});

builder.Services.AddSingleton<EnrollmentBook>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { lab = "Step07_AuthnAuthzEntry" }));

app.MapGet("/me", (ClaimsPrincipal user) =>
{
    var claims = user.Claims.Select(c => new { c.Type, c.Value }).ToList();
    return Results.Ok(new
    {
        sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier),
        roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).Concat(user.FindAll("role").Select(c => c.Value)).Distinct(),
        college_id = user.FindFirstValue("college_id"),
        claims,
    });
}).RequireAuthorization();

app.MapPost("/api/v1/courses", (CreateCourseRequest request) =>
    Results.Created($"/api/v1/courses/{Guid.NewGuid()}", request))
    .RequireAuthorization("AdminOnly");

app.MapPost("/api/v1/enrollments", (CreateEnrollmentRequest request, ClaimsPrincipal user, EnrollmentBook book) =>
{
    var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
    var enrollment = book.Add(request.StudentId == Guid.Empty ? Guid.Parse(PadGuid(sub)) : request.StudentId, request.SectionId);
    return Results.Created($"/api/v1/enrollments/{enrollment.Id}", enrollment);
}).RequireAuthorization("CanEnroll");

app.MapGet("/api/v1/enrollments/public-count", (EnrollmentBook book) => Results.Ok(new { count = book.Count }));

app.MapPost("/token/dev", (DevTokenRequest body) =>
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var claims = new List<Claim>
    {
        new("sub", body.Sub),
        new(ClaimTypes.NameIdentifier, body.Sub),
        new(ClaimTypes.Role, body.Role),
        new("role", body.Role),
        new("college_id", body.CollegeId),
    };
    var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.UtcNow.AddHours(2), signingCredentials: creds);
    return Results.Ok(new { access_token = new JwtSecurityTokenHandler().WriteToken(token) });
});

app.Run();

static string PadGuid(string value)
{
    var hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
    return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
}

public partial class Program;

public sealed record DevTokenRequest(string Sub, string Role, string CollegeId);

public sealed class EnrollmentBook
{
    private int _count;
    public int Count => _count;

    public EnrollmentDto Add(Guid studentId, Guid sectionId)
    {
        Interlocked.Increment(ref _count);
        return new EnrollmentDto(Guid.NewGuid(), studentId, sectionId, EnrollmentStatus.Confirmed, DateTimeOffset.UtcNow);
    }
}
