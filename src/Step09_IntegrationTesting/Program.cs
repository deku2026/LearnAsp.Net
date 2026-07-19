// LearnAspNet
// Doc   : ASP.NetStudy/步骤9-集成测试-完整实施指南.md
// Part  : Step09 · IntegrationTesting
// Title : 集成测试

using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Campus.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Step09_IntegrationTesting.Data;

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration.GetConnectionString("Postgres")
         ?? "Host=localhost;Port=5432;Database=campus_step09;Username=dotnet;Password=dotnet_dev";

builder.Services.AddDbContext<CampusDbContext>(o => o.UseNpgsql(cs));

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is required outside Development. Use an environment variable or secret store.");
}

var jwtKeyBytes = string.IsNullOrWhiteSpace(jwtSigningKey)
    ? RandomNumberGenerator.GetBytes(32)
    : Encoding.UTF8.GetBytes(jwtSigningKey);
if (jwtKeyBytes.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "campus-step09",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "campus-api",
            IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
            NameClaimType = "sub",
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    o.AddPolicy("CanEnroll", p => p.RequireRole("Student", "Admin"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Database:ApplyMigrations", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CampusDbContext>();
    // Production deployments should run migrations as a separate controlled step.
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { lab = "Step09_IntegrationTesting", storage = "PostgreSQL" }));

if (app.Environment.IsDevelopment())
{
    app.MapPost("/token/dev", (DevTokenRequest body) =>
    {
        if (string.IsNullOrWhiteSpace(body.Sub) || body.Role is not ("Student" or "Admin"))
        {
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed });
        }

        var claims = new[]
        {
            new Claim("sub", body.Sub),
            new Claim(ClaimTypes.NameIdentifier, body.Sub),
            new Claim(ClaimTypes.Role, body.Role),
            new Claim("role", body.Role),
        };
        var token = new JwtSecurityToken(
            builder.Configuration["Jwt:Issuer"] ?? "campus-step09",
            builder.Configuration["Jwt:Audience"] ?? "campus-api",
            claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(jwtKeyBytes),
                SecurityAlgorithms.HmacSha256));
        return Results.Ok(new { access_token = new JwtSecurityTokenHandler().WriteToken(token) });
    });
}

var api = app.MapGroup("/api/v1");

api.MapGet("/courses", async (string? q, CampusDbContext db) =>
{
    var query = db.Courses.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(q))
    {
        query = query.Where(c => c.Code.Contains(q) || c.Title.Contains(q));
    }

    var list = await query.OrderBy(c => c.Code)
        .Select(c => new CourseDto(c.Id, c.Code, c.Title, c.Credits))
        .ToListAsync();
    return Results.Ok(list);
});

api.MapGet("/courses/{id:guid}", async (Guid id, CampusDbContext db) =>
{
    var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    return course is null
        ? Results.NotFound(new { errorCode = ErrorCodes.NotFound })
        : Results.Ok(new CourseDto(course.Id, course.Code, course.Title, course.Credits));
});

api.MapPost("/courses", async (CreateCourseBody body, CampusDbContext db) =>
{
    if (!MiniValidator.TryValidate(body, out var errors))
    {
        return Results.ValidationProblem(errors, extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = ErrorCodes.ValidationFailed,
        });
    }

    var row = new CourseRow
    {
        Id = Guid.NewGuid(),
        Code = body.Code.Trim(),
        Title = body.Title.Trim(),
        Credits = body.Credits,
    };
    db.Courses.Add(row);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        return Results.Conflict(new { errorCode = "course.code_conflict" });
    }

    return Results.Created($"/api/v1/courses/{row.Id}", new CourseDto(row.Id, row.Code, row.Title, row.Credits));
}).RequireAuthorization("AdminOnly");

api.MapPut("/courses/{id:guid}", async (Guid id, UpdateCourseBody body, CampusDbContext db) =>
{
    if (!MiniValidator.TryValidate(body, out var errors))
    {
        return Results.ValidationProblem(errors, extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = ErrorCodes.ValidationFailed,
        });
    }

    var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id);
    if (course is null)
    {
        return Results.NotFound(new { errorCode = ErrorCodes.NotFound });
    }

    course.Title = body.Title.Trim();
    course.Credits = body.Credits;
    await db.SaveChangesAsync();
    return Results.Ok(new CourseDto(course.Id, course.Code, course.Title, course.Credits));
}).RequireAuthorization("AdminOnly");

api.MapDelete("/courses/{id:guid}", async (Guid id, CampusDbContext db) =>
{
    var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id);
    if (course is null)
    {
        return Results.NotFound(new { errorCode = ErrorCodes.NotFound });
    }

    db.Courses.Remove(course);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
    {
        return Results.Conflict(new { errorCode = "course.has_sections" });
    }

    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

api.MapPost("/sections", async (CreateSectionBody body, CampusDbContext db) =>
{
    if (!MiniValidator.TryValidate(body, out var errors))
    {
        return Results.ValidationProblem(errors, extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = ErrorCodes.ValidationFailed,
        });
    }

    var courseExists = await db.Courses.AnyAsync(c => c.Id == body.CourseId);
    if (!courseExists)
    {
        return Results.NotFound(new { errorCode = ErrorCodes.NotFound });
    }

    var row = new SectionRow
    {
        Id = Guid.NewGuid(),
        CourseId = body.CourseId,
        Term = body.Term.Trim(),
        Capacity = body.Capacity,
        SeatsRemaining = body.Capacity,
    };
    db.Sections.Add(row);
    await db.SaveChangesAsync();
    return Results.Created(
        $"/api/v1/sections/{row.Id}",
        new SectionDto(row.Id, row.CourseId, row.Term, row.Capacity, row.SeatsRemaining));
}).RequireAuthorization("AdminOnly");

api.MapPost("/enrollments", async (CreateEnrollmentRequest body, CampusDbContext db, ClaimsPrincipal user) =>
{
    var authenticatedStudentId = Guid.Parse(StableGuid(
        user.FindFirstValue("sub") ??
        user.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new InvalidOperationException("Authenticated user has no subject claim.")));
    if (!user.IsInRole("Admin") &&
        body.StudentId != Guid.Empty &&
        body.StudentId != authenticatedStudentId)
    {
        return Results.Forbid();
    }

    var studentId = body.StudentId == Guid.Empty ? authenticatedStudentId : body.StudentId;

    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var section = await db.Sections
        .FromSqlInterpolated($"SELECT * FROM sections WHERE \"Id\" = {body.SectionId} FOR UPDATE")
        .SingleOrDefaultAsync();
    if (section is null)
    {
        return Results.NotFound(new { errorCode = ErrorCodes.NotFound });
    }

    var dup = await db.Enrollments.AnyAsync(e =>
        e.StudentId == studentId && e.SectionId == body.SectionId && e.Status != nameof(EnrollmentStatus.Cancelled));
    if (dup)
    {
        return Results.Conflict(new { errorCode = ErrorCodes.EnrollmentDuplicate });
    }

    string status;
    if (section.SeatsRemaining > 0)
    {
        status = nameof(EnrollmentStatus.Confirmed);
        section.SeatsRemaining--;
    }
    else
    {
        status = nameof(EnrollmentStatus.Waitlisted);
    }

    var row = new EnrollmentRow
    {
        Id = Guid.NewGuid(),
        StudentId = studentId,
        SectionId = body.SectionId,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.Enrollments.Add(row);
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    return Results.Created(
        $"/api/v1/enrollments/{row.Id}",
        new EnrollmentDto(row.Id, row.StudentId, row.SectionId, Enum.Parse<EnrollmentStatus>(row.Status), row.CreatedAt));
}).RequireAuthorization("CanEnroll");

api.MapGet("/enrollments", async (Guid? studentId, CampusDbContext db, ClaimsPrincipal user) =>
{
    var authenticatedStudentId = Guid.Parse(StableGuid(
        user.FindFirstValue("sub") ??
        user.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new InvalidOperationException("Authenticated user has no subject claim.")));
    if (!user.IsInRole("Admin"))
    {
        if (studentId is not null && studentId != authenticatedStudentId)
        {
            return Results.Forbid();
        }

        studentId = authenticatedStudentId;
    }

    var q = db.Enrollments.AsNoTracking().AsQueryable();
    if (studentId is not null)
    {
        q = q.Where(e => e.StudentId == studentId);
    }

    var list = await q.OrderByDescending(e => e.CreatedAt)
        .Select(e => new EnrollmentDto(
            e.Id,
            e.StudentId,
            e.SectionId,
            Enum.Parse<EnrollmentStatus>(e.Status),
            e.CreatedAt))
        .ToListAsync();
    return Results.Ok(list);
}).RequireAuthorization();

app.Run();

static string StableGuid(string value)
{
    var hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..32];
    return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
}

public partial class Program;

public sealed record DevTokenRequest(string Sub, string Role);

public sealed class CreateCourseBody
{
    [Required, MinLength(2), MaxLength(16)]
    public string Code { get; set; } = "";

    [Required, MinLength(2)]
    public string Title { get; set; } = "";

    [Range(1, 10)]
    public int Credits { get; set; }
}

public sealed class CreateSectionBody
{
    [Required]
    public Guid CourseId { get; set; }

    [Required, MinLength(2)]
    public string Term { get; set; } = "";

    [Range(1, 500)]
    public int Capacity { get; set; }
}

public sealed class UpdateCourseBody
{
    [Required, MinLength(2)]
    public string Title { get; set; } = "";

    [Range(1, 10)]
    public int Credits { get; set; }
}

public static class MiniValidator
{
    public static bool TryValidate<T>(T model, out Dictionary<string, string[]> errors)
    {
        var ctx = new ValidationContext(model!);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(model!, ctx, results, validateAllProperties: true);
        errors = results
            .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "invalid").ToArray());
        return ok;
    }
}
