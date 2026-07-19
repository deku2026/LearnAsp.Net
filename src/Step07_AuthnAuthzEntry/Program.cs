// LearnAspNet
// Doc   : ASP.NetStudy/步骤7-认证授权接入点-完整实施指南.md
// Part  : Step07 · AuthnAuthzEntry
// Title : 认证授权接入点

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Campus.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });
builder.Services
    .AddOptions<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
    {
        var settings = JwtLabSettings.Create(configuration, environment);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(settings.SigningKeyBytes),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Fallback policy: secure by default — all endpoints require auth unless [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    options.AddPolicy("CanEnroll", p => p.RequireRole("Student", "Admin"));
});

builder.Services.AddSingleton<EnrollmentBook>();

var app = builder.Build();
var jwtSettings = JwtLabSettings.Create(app.Configuration, app.Environment);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { lab = "Step07_AuthnAuthzEntry" })).AllowAnonymous();

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
    var authenticatedStudentId = Guid.Parse(PadGuid(sub));
    if (request.StudentId != Guid.Empty && request.StudentId != authenticatedStudentId)
    {
        return Results.Forbid();
    }

    var enrollment = book.Add(authenticatedStudentId, request.SectionId);
    return Results.Created($"/api/v1/enrollments/{enrollment.Id}", enrollment);
}).RequireAuthorization("CanEnroll");

app.MapGet("/api/v1/default-protected", () => Results.Ok(new { securedBy = "fallback-policy" }));
app.MapGet("/api/v1/enrollments/public-count", (EnrollmentBook book) => Results.Ok(new { count = book.Count })).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapPost("/token/dev", (DevTokenRequest body) =>
    {
        if (string.IsNullOrWhiteSpace(body.Sub) ||
            body.Role is not ("Student" or "Admin") ||
            string.IsNullOrWhiteSpace(body.CollegeId))
        {
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed });
        }

        var key = new SymmetricSecurityKey(jwtSettings.SigningKeyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", body.Sub),
            new(ClaimTypes.NameIdentifier, body.Sub),
            new(ClaimTypes.Role, body.Role),
            new("role", body.Role),
            new("college_id", body.CollegeId),
        };
        var token = new JwtSecurityToken(
            jwtSettings.Issuer,
            jwtSettings.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);
        return Results.Ok(new { access_token = new JwtSecurityTokenHandler().WriteToken(token) });
    }).AllowAnonymous();
}

app.Run();

static string PadGuid(string value)
{
    var hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];
    return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
}

public partial class Program;

public sealed record DevTokenRequest(string Sub, string Role, string CollegeId);

public sealed record JwtLabSettings(string Issuer, string Audience, byte[] SigningKeyBytes)
{
    private static readonly byte[] DevelopmentSigningKey = RandomNumberGenerator.GetBytes(32);

    public static JwtLabSettings Create(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("Jwt");
        var configuredSigningKey = section["SigningKey"];
        byte[] signingKeyBytes;
        if (string.IsNullOrWhiteSpace(configuredSigningKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey is required outside Development. Use an environment variable or secret store.");
            }

            signingKeyBytes = DevelopmentSigningKey;
        }
        else
        {
            if (Encoding.UTF8.GetByteCount(configuredSigningKey) < 32)
            {
                throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
            }

            signingKeyBytes = Encoding.UTF8.GetBytes(configuredSigningKey);
        }

        return new JwtLabSettings(
            section["Issuer"] ?? "campus-dev",
            section["Audience"] ?? "campus-api",
            signingKeyBytes);
    }
}

public sealed class EnrollmentBook
{
    private int _count;
    public int Count => Volatile.Read(ref _count);

    public EnrollmentDto Add(Guid studentId, Guid sectionId)
    {
        Interlocked.Increment(ref _count);
        return new EnrollmentDto(Guid.NewGuid(), studentId, sectionId, EnrollmentStatus.Confirmed, DateTimeOffset.UtcNow);
    }
}
