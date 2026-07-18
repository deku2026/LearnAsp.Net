// LearnAspNet
// Doc   : ASP.NetStudy/步骤9-集成测试-完整实施指南.md
// Part  : Step09 · IntegrationTesting
// Title : 集成测试

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Campus.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Step09_IntegrationTesting;
using Step09_IntegrationTesting.Data;

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration.GetConnectionString("Postgres")
         ?? "Host=localhost;Port=5432;Database=campus_step09;Username=dotnet;Password=dotnet_dev";

builder.Services.AddDbContext<CampusDbContext>(o => o.UseNpgsql(cs));

builder.Services
    .AddAuthentication(DevTestAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevTestAuthHandler>(DevTestAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    o.AddPolicy("CanEnroll", p => p.RequireRole("Student", "Admin"));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampusDbContext>();
    // Use migrations (not EnsureCreated) so schema matches production and __EFMigrationsHistory is present.
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { lab = "Step09_IntegrationTesting", storage = "PostgreSQL" }));

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
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/courses/{row.Id}", new CourseDto(row.Id, row.Code, row.Title, row.Credits));
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
    var section = await db.Sections.FirstOrDefaultAsync(s => s.Id == body.SectionId);
    if (section is null)
    {
        return Results.NotFound(new { errorCode = ErrorCodes.NotFound });
    }

    var studentId = body.StudentId == Guid.Empty
        ? Guid.Parse(StableGuid(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon"))
        : body.StudentId;

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
    return Results.Created(
        $"/api/v1/enrollments/{row.Id}",
        new EnrollmentDto(row.Id, row.StudentId, row.SectionId, Enum.Parse<EnrollmentStatus>(row.Status), row.CreatedAt));
}).RequireAuthorization("CanEnroll");

api.MapGet("/enrollments", async (Guid? studentId, CampusDbContext db) =>
{
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
