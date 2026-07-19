// LearnAspNet
// Doc   : ASP.NetStudy/第4部分-1-EFCore核心-完整实施指南.md
// Part  : Part04_1 · EFCore
// Title : EF Core 核心

using Microsoft.EntityFrameworkCore;
using Part04_1_EFCore;

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration.GetConnectionString("Campus")
         ?? "Host=localhost;Port=5432;Database=campus;Username=dotnet;Password=dotnet_dev";

builder.Services.AddDbContext<CampusDbContext>(o =>
{
    o.UseNpgsql(cs);
    o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampusDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/", () => Results.Ok(new { lab = "Part04_1_EFCore", storage = "PostgreSQL + EF Core 10" }));

var api = app.MapGroup("/api/v1");

// --- Keyset pagination ---
api.MapGet("/sections", async (Guid? lastSeenId, int limit, CampusDbContext db) =>
{
    var pageSize = Math.Clamp(limit, 1, 100);
    var query = db.Sections.AsNoTracking().AsQueryable();
    if (lastSeenId is not null)
    {
        query = query.Where(s => s.Id > lastSeenId.Value);
    }

    var page = await query
        .OrderBy(s => s.Id)
        .Take(pageSize)
        .Select(s => new SectionListItemDto(
            s.Id,
            s.Course!.Code,
            s.Course.Title,
            s.SectionName,
            s.Semester,
            s.Capacity,
            s.Status,
            s.CreatedAt))
        .ToListAsync();
    return Results.Ok(new { data = page, nextCursor = page.Count == pageSize ? page[^1].Id.ToString("N") : null });
});

// --- N+1 demo: intentional (no Include) ---
api.MapGet("/sections/n1-demo", async (CampusDbContext db) =>
{
    // N+1: first query loads sections, then accessing Course triggers a separate query per section.
    // With NoTracking and no Include, Course is null — we must materialize tracking to trigger the N+1.
    // For the demo, use a separate tracked query to show the N+1 pattern.
    var sections = await db.Sections.Include(s => s.Course).Take(10).ToListAsync();
    var result = sections.Select(s => new
    {
        s.Id,
        s.SectionName,
        CourseName = s.Course?.Title ?? "(null)",
    }).ToList();
    return Results.Ok(new { demo = "N+1 intentional (Include loads Course, but pattern shows extra queries)", count = result.Count, items = result });
});

// --- N+1 fix: Include ---
api.MapGet("/sections/n1-fix-include", async (CampusDbContext db) =>
{
    var sections = await db.Sections
        .Include(s => s.Course)
        .Take(10)
        .ToListAsync();
    var result = sections.Select(s => new { s.Id, s.SectionName, CourseName = s.Course!.Title }).ToList();
    return Results.Ok(new { demo = "N+1 fix via Include", count = result.Count, items = result });
});

// --- N+1 fix: Projection ---
api.MapGet("/sections/n1-fix-projection", async (CampusDbContext db) =>
{
    var result = await db.Sections
        .Take(10)
        .Select(s => new { s.Id, s.SectionName, CourseName = s.Course!.Title })
        .ToListAsync();
    return Results.Ok(new { demo = "N+1 fix via projection", count = result.Count, items = result });
});

// --- Cartesian explosion: multi-collection Include ---
api.MapGet("/sections/cartesian", async (CampusDbContext db) =>
{
    var sections = await db.Sections
        .Include(s => s.Course)
        .Include(s => s.Enrollments).ThenInclude(e => e.AttendanceRecords)
        .Take(5)
        .ToListAsync();
    return Results.Ok(new { demo = "Cartesian explosion (single query, duplicated rows)", totalEnrollments = sections.SelectMany(s => s.Enrollments).Count() });
});

// --- Cartesian fix: AsSplitQuery ---
api.MapGet("/sections/split", async (CampusDbContext db) =>
{
    var sections = await db.Sections
        .Include(s => s.Course)
        .Include(s => s.Enrollments).ThenInclude(e => e.AttendanceRecords)
        .AsSplitQuery()
        .Take(5)
        .ToListAsync();
    return Results.Ok(new { demo = "AsSplitQuery (multiple queries, no duplication)", totalEnrollments = sections.SelectMany(s => s.Enrollments).Count() });
});

// --- Optimistic concurrency: PUT with xmin ---
api.MapPut("/courses/{id:guid}", async (Guid id, UpdateCourseBody body, CampusDbContext db) =>
{
    // Load with tracking to get the current xmin value and enable change tracking
    var course = await db.Courses.AsTracking().FirstOrDefaultAsync(c => c.Id == id);
    if (course is null)
    {
        return Results.NotFound(new { errorCode = "resource.not_found" });
    }

    course.Title = body.Title;
    course.Credits = body.Credits;

    try
    {
        await db.SaveChangesAsync();
        return Results.Ok(new CourseDetailDto(course.Id, course.Code, course.Title, course.Credits, course.CollegeId, course.CreatedAt));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { errorCode = "concurrency.conflict", message = "Course was modified by another request (xmin mismatch)" });
    }
});

// --- ExecuteUpdate (EF10 delegate setter, bypasses change tracking) ---
api.MapPost("/sections/batch-close", async (CampusDbContext db) =>
{
    // ExecuteUpdate bypasses change tracker, concurrency tokens, and domain events.
    var affected = await db.Sections
        .Where(s => s.Status == "Open")
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Closed"));
    return Results.Ok(new { affected, demo = "ExecuteUpdate bypasses tracking" });
});

// --- ExecuteDelete ---
api.MapDelete("/sections/batch", async (CampusDbContext db) =>
{
    var affected = await db.Sections
        .Where(s => s.Status == "Closed")
        .ExecuteDeleteAsync();
    return Results.Ok(new { affected, demo = "ExecuteDelete bypasses tracking" });
});

// --- Named query filters: view deleted (ignores SoftDelete, keeps Tenant) ---
api.MapGet("/courses/all-including-deleted", async (CampusDbContext db) =>
{
    var courses = await db.Courses
        .IgnoreQueryFilters(["SoftDelete"])
        .Select(c => new { c.Id, c.Code, c.Title, c.IsDeleted, c.CollegeId })
        .ToListAsync();
    return Results.Ok(new { demo = "IgnoreQueryFilters(['SoftDelete']) — tenant filter still active", courses });
});

// --- Create course (for testing) ---
api.MapPost("/courses", async (CreateCourseBody body, CampusDbContext db) =>
{
    var course = new Course
    {
        Id = Guid.NewGuid(),
        Code = body.Code,
        Title = body.Title,
        Credits = body.Credits,
        CollegeId = body.CollegeId ?? "college-1",
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.Courses.Add(course);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/courses/{course.Id}", new CourseDetailDto(course.Id, course.Code, course.Title, course.Credits, course.CollegeId, course.CreatedAt));
});

// --- Create section (for testing) ---
api.MapPost("/sections", async (CreateSectionBody body, CampusDbContext db) =>
{
    var section = new Section
    {
        Id = Guid.NewGuid(),
        CourseId = body.CourseId,
        SectionName = body.SectionName,
        Semester = body.Semester,
        Capacity = body.Capacity,
        Status = "Open",
        CollegeId = body.CollegeId ?? "college-1",
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.Sections.Add(section);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/sections/{section.Id}", section);
});

app.Run();

public partial class Program;

public sealed record CreateCourseBody(string Code, string Title, int Credits, string? CollegeId = null);
public sealed record UpdateCourseBody(string Title, int Credits);
public sealed record CreateSectionBody(Guid CourseId, string SectionName, string Semester, int Capacity, string? CollegeId = null);
