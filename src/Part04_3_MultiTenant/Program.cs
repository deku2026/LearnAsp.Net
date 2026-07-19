// LearnAspNet
// Doc   : ASP.NetStudy/第4部分-3-多租户-完整实施指南.md
// Part  : Part04_3 · MultiTenant
// Title : 多租户 · 行级隔离 · query filter · 写保护

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Part04_3_MultiTenant;

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration.GetConnectionString("Campus")
         ?? "Host=localhost;Port=5432;Database=campus_tenant;Username=dotnet;Password=dotnet_dev";

// Tenant context: scoped, same instance for ITenantContext + ITenantSetter
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<ITenantSetter>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddHybridCache();

builder.Services.AddDbContext<TenantDbContext>((sp, o) =>
{
    o.UseNpgsql(cs);
    o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Database:ApplyMigrations", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
    await db.Database.MigrateAsync();
}

// Tenant resolution: after auth, before endpoints
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part04_3_MultiTenant",
    model = "shared-db + tenant-id + EF10 named query filters",
    resolution = "JWT claim college_id → X-Tenant-Id header → default college-1",
}));

var api = app.MapGroup("/api/v1");

// List courses — auto-filtered by tenant via query filter
api.MapGet("/courses", async (TenantDbContext db) =>
{
    var list = await db.Courses
        .Select(c => new CourseDto(c.Id, c.Code, c.Title, c.Credits, c.CollegeId))
        .ToListAsync();
    return Results.Ok(list);
});

// Create course — SaveChanges interceptor stamps CollegeId automatically
api.MapPost("/courses", async (CreateCourseBody body, TenantDbContext db) =>
{
    var course = new Course
    {
        Id = Guid.NewGuid(),
        Code = body.Code,
        Title = body.Title,
        Credits = body.Credits,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    db.Courses.Add(course);
    await db.SaveChangesAsync();
    return Results.Created($"/api/v1/courses/{course.Id}", new CourseDto(course.Id, course.Code, course.Title, course.Credits, course.CollegeId));
});

// Get course by id — query filter ensures tenant isolation (returns 404 if wrong tenant)
api.MapGet("/courses/{id:guid}", async (
    Guid id,
    ITenantContext tenant,
    TenantDbContext db,
    HybridCache cache,
    CancellationToken ct) =>
{
    var tenantId = tenant.CurrentCollegeId ?? throw new InvalidOperationException("No tenant context set.");
    var course = await cache.GetOrCreateAsync(
        TenantCacheKey.Course(tenantId, id),
        async token =>
        {
            var row = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, token);
            return row is null
                ? null
                : new CourseDto(row.Id, row.Code, row.Title, row.Credits, row.CollegeId);
        },
        tags: [TenantCacheKey.CoursesTag(tenantId)],
        cancellationToken: ct);
    return course is null
        ? Results.NotFound(new { errorCode = "resource.not_found" })
        : Results.Ok(course);
});

// Admin: view including deleted — IgnoreQueryFilters(["SoftDelete"]) keeps tenant isolation
api.MapGet("/courses/all-including-deleted", async (TenantDbContext db) =>
{
    var list = await db.Courses
        .IgnoreQueryFilters(["SoftDelete"])
        .Select(c => new { c.Id, c.Code, c.Title, c.CollegeId, c.IsDeleted })
        .ToListAsync();
    return Results.Ok(new { demo = "IgnoreQueryFilters(['SoftDelete']) — tenant filter still active", courses = list });
});

// Soft-delete a course (for testing the filter)
api.MapDelete("/courses/{id:guid}", async (
    Guid id,
    ITenantContext tenant,
    TenantDbContext db,
    HybridCache cache,
    CancellationToken ct) =>
{
    var tenantId = tenant.CurrentCollegeId ?? throw new InvalidOperationException("No tenant context set.");
    var course = await db.Courses.AsTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    if (course is null) return Results.NotFound();
    course.IsDeleted = true;
    await db.SaveChangesAsync(ct);
    await cache.RemoveAsync(TenantCacheKey.Course(tenantId, id), ct);
    await cache.RemoveByTagAsync(TenantCacheKey.CoursesTag(tenantId), ct);
    return Results.Ok(new { softDeleted = true });
});

app.Run();

public partial class Program;
