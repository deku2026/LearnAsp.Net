// LearnAspNet
// Doc   : ASP.NetStudy/第4部分-1-EFCore核心-完整实施指南.md
// Part  : Part04_1 · EFCore
// Title : EF Core 核心

using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Part04_1_EFCore;

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration.GetConnectionString("Campus")
         ?? "Host=localhost;Port=5432;Database=campus;Username=dotnet;Password=dotnet_dev";

builder.Services.AddScoped<QueryCounterInterceptor>();
builder.Services.AddDbContext<CampusDbContext>((sp, o) =>
{
    o.UseNpgsql(cs);
    o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    o.AddInterceptors(sp.GetRequiredService<QueryCounterInterceptor>());
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Database:ApplyMigrations", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CampusDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/", () => Results.Ok(new { lab = "Part04_1_EFCore", storage = "PostgreSQL + EF Core 10" }));

var api = app.MapGroup("/api/v1");

// --- Keyset pagination ---
api.MapGet("/sections", async (string? after, int? limit, CampusDbContext db) =>
{
    var pageSize = Math.Clamp(limit ?? 20, 1, 100);
    var query = db.Sections.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(after))
    {
        if (!TryDecodeCursor(after, out var createdAt, out var id))
        {
            return Results.BadRequest(new { errorCode = "pagination.cursor_invalid" });
        }

        query = query.Where(s => s.CreatedAt > createdAt || (s.CreatedAt == createdAt && s.Id > id));
    }

    var rows = await query
        .OrderBy(s => s.CreatedAt)
        .ThenBy(s => s.Id)
        .Take(pageSize + 1)
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
    var hasMore = rows.Count > pageSize;
    var page = rows.Take(pageSize).ToList();
    var nextCursor = hasMore ? EncodeCursor(page[^1].CreatedAt, page[^1].Id) : null;
    return Results.Ok(new { data = page, nextCursor, hasMore });
});

// --- N+1 demo: intentional 1 query for sections + N queries for course titles ---
api.MapGet("/sections/n1-demo", async (CampusDbContext db, QueryCounterInterceptor counter) =>
{
    counter.Reset();
    var sections = await db.Sections
        .OrderBy(s => s.Id)
        .Take(10)
        .Select(s => new { s.Id, s.SectionName, s.CourseId })
        .ToListAsync();
    var result = new List<object>(sections.Count);
    foreach (var section in sections)
    {
        var courseName = await db.Courses
            .Where(c => c.Id == section.CourseId)
            .Select(c => c.Title)
            .SingleAsync();
        result.Add(new { section.Id, section.SectionName, CourseName = courseName });
    }

    return Results.Ok(new
    {
        demo = "N+1 intentional",
        count = result.Count,
        queryCount = counter.Count,
        items = result,
    });
});

// --- N+1 fix: Include ---
api.MapGet("/sections/n1-fix-include", async (CampusDbContext db, QueryCounterInterceptor counter) =>
{
    counter.Reset();
    var sections = await db.Sections
        .Include(s => s.Course)
        .Take(10)
        .ToListAsync();
    var result = sections.Select(s => new { s.Id, s.SectionName, CourseName = s.Course!.Title }).ToList();
    return Results.Ok(new { demo = "N+1 fix via Include", count = result.Count, queryCount = counter.Count, items = result });
});

// --- N+1 fix: Projection ---
api.MapGet("/sections/n1-fix-projection", async (CampusDbContext db, QueryCounterInterceptor counter) =>
{
    counter.Reset();
    var result = await db.Sections
        .Take(10)
        .Select(s => new { s.Id, s.SectionName, CourseName = s.Course!.Title })
        .ToListAsync();
    return Results.Ok(new { demo = "N+1 fix via projection", count = result.Count, queryCount = counter.Count, items = result });
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
    db.Entry(course).Property<uint>("xmin").OriginalValue = body.Version;

    try
    {
        await db.SaveChangesAsync();
        var version = db.Entry(course).Property<uint>("xmin").CurrentValue;
        return Results.Ok(new CourseDetailDto(
            course.Id,
            course.Code,
            course.Title,
            course.Credits,
            course.CollegeId,
            course.CreatedAt,
            version));
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
    var version = db.Entry(course).Property<uint>("xmin").CurrentValue;
    return Results.Created(
        $"/api/v1/courses/{course.Id}",
        new CourseDetailDto(
            course.Id,
            course.Code,
            course.Title,
            course.Credits,
            course.CollegeId,
            course.CreatedAt,
            version));
});

api.MapGet("/courses/{id:guid}", async (Guid id, CampusDbContext db) =>
{
    var course = await db.Courses
        .Where(c => c.Id == id)
        .Select(c => new CourseDetailDto(
            c.Id,
            c.Code,
            c.Title,
            c.Credits,
            c.CollegeId,
            c.CreatedAt,
            EF.Property<uint>(c, "xmin")))
        .SingleOrDefaultAsync();
    return course is null ? Results.NotFound() : Results.Ok(course);
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

static string EncodeCursor(DateTimeOffset createdAt, Guid id)
{
    var raw = string.Create(CultureInfo.InvariantCulture, $"{createdAt.UtcTicks}:{id:N}");
    return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
}

static bool TryDecodeCursor(string raw, out DateTimeOffset createdAt, out Guid id)
{
    createdAt = default;
    id = default;
    try
    {
        var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(raw));
        var parts = decoded.Split(':', 2);
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
            !Guid.TryParseExact(parts[1], "N", out id))
        {
            return false;
        }

        createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }
    catch (FormatException)
    {
        return false;
    }
}

public partial class Program;

public sealed record CreateCourseBody(string Code, string Title, int Credits, string? CollegeId = null);
public sealed record UpdateCourseBody(string Title, int Credits, uint Version);
public sealed record CreateSectionBody(Guid CourseId, string SectionName, string Semester, int Capacity, string? CollegeId = null);
