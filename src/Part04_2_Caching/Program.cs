// LearnAspNet
// Doc   : ASP.NetStudy/第4部分-2-缓存三层-完整实施指南.md
// Part  : Part04_2 · Caching
// Title : 缓存三层 · IMemoryCache · IDistributedCache(Redis) · HybridCache · Output Cache

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Part04_2_Caching;

var builder = WebApplication.CreateBuilder(args);

var pgCs = builder.Configuration.GetConnectionString("Campus")
           ?? "Host=localhost;Port=5432;Database=campus_cache;Username=dotnet;Password=dotnet_dev";
var redisCs = builder.Configuration.GetConnectionString("Redis")
              ?? "localhost:6380,abortConnect=false";

// EF Core
builder.Services.AddDbContext<CacheDbContext>(o =>
{
    o.UseNpgsql(pgCs);
    o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

// L1: IMemoryCache (for comparison/benchmark — no stampede protection)
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CacheQueryMetrics>();

// HybridCache: L1 (memory) — L2 (Redis) optional via config
// Redis L2 requires AddStackExchangeRedisCache; if Redis is down, HybridCache falls back to L1 only.
var useRedisL2 = builder.Configuration.GetValue("Cache:UseRedisL2", true);
if (useRedisL2)
{
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redisCs;
        o.InstanceName = "Campus:";
    });
}

builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        LocalCacheExpiration = TimeSpan.FromMinutes(5),
        Expiration = TimeSpan.FromMinutes(30),
    };
});

// Output Cache (in-memory by default; Redis output cache backend not available in .NET 10 yet)
builder.Services.AddOutputCache(o =>
{
    o.AddPolicy("Sections", b => b
        .Expire(TimeSpan.FromMinutes(5))
        .SetVaryByQuery("page", "limit")
        .Tag("sections"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Database:ApplyMigrations", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CacheDbContext>();
    await db.Database.MigrateAsync();
}

// Middleware order: UseRouting → UseOutputCache → UseAuthorization
app.UseRouting();
app.UseOutputCache();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part04_2_Caching",
    layers = new[] { "L1: IMemoryCache", "L2: Redis IDistributedCache", "HybridCache (L1+L2+stampede)", "Output Cache" },
    redisL2Enabled = useRedisL2,
}));

var api = app.MapGroup("/api/v1");

// --- HybridCache: get course by id with tag-based invalidation ---
api.MapGet("/courses/{id:guid}", async (
    Guid id,
    HttpContext http,
    HybridCache hybrid,
    CacheDbContext db,
    CacheQueryMetrics metrics,
    CancellationToken ct) =>
{
    var tenantId = TenantId(http);
    var dto = await hybrid.GetOrCreateAsync(
        $"tenant:{tenantId}:course:{id}",
        async token =>
        {
            metrics.Increment("course");
            var course = await db.Courses.FirstOrDefaultAsync(
                c => c.Id == id && c.CollegeId == tenantId,
                token);
            return course is null ? null : new CourseDto(course.Id, course.Code, course.Title, course.Credits, course.CollegeId);
        },
        tags: [$"tenant:{tenantId}:courses"],
        cancellationToken: ct);

    return dto is null ? Results.NotFound() : Results.Ok(dto);
});

// --- Write-through invalidation: update → evict by key + tag ---
api.MapPut("/courses/{id:guid}", async (
    Guid id,
    UpdateCourseBody body,
    HttpContext http,
    CacheDbContext db,
    HybridCache hybrid,
    CancellationToken ct) =>
{
    var tenantId = TenantId(http);
    var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.CollegeId == tenantId, ct);
    if (course is null) return Results.NotFound();

    // Use ExecuteUpdate to bypass xmin concurrency token (cache lab doesn't need concurrency)
    await db.Courses.Where(c => c.Id == id)
        .ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Title, body.Title)
            .SetProperty(c => c.Credits, body.Credits), ct);

    // Invalidate by key AND by tag (belt + suspenders for L1+L2)
    await hybrid.RemoveAsync($"tenant:{tenantId}:course:{id}", ct);
    await hybrid.RemoveByTagAsync($"tenant:{tenantId}:courses", ct);
    return Results.Ok(new CourseDto(course.Id, course.Code, body.Title, body.Credits, course.CollegeId));
});

// --- Create course (for testing) ---
api.MapPost("/courses", async (CreateCourseBody body, HttpContext http, CacheDbContext db, CancellationToken ct) =>
{
    var tenantId = http.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                   ?? body.CollegeId
                   ?? "college-1";
    var course = new Course
    {
        Id = Guid.NewGuid(),
        Code = body.Code,
        Title = body.Title,
        Credits = body.Credits,
        CollegeId = tenantId,
    };
    db.Courses.Add(course);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/courses/{course.Id}", new CourseDto(course.Id, course.Code, course.Title, course.Credits, course.CollegeId));
});

// --- IMemoryCache demo (no stampede protection, for contrast) ---
api.MapGet("/memory-cache/{id:guid}", async (
    Guid id,
    HttpContext http,
    IMemoryCache mem,
    CacheDbContext db,
    CacheQueryMetrics metrics,
    CancellationToken ct) =>
{
    var tenantId = TenantId(http);
    var key = $"mem:tenant:{tenantId}:course:{id}";
    if (mem.TryGetValue(key, out CourseDto? cached) && cached is not null)
        return Results.Ok(new { source = "memory", data = cached });

    metrics.Increment("memory");
    var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id && c.CollegeId == tenantId, ct);
    if (course is null) return Results.NotFound();

    var dto = new CourseDto(course.Id, course.Code, course.Title, course.Credits, course.CollegeId);
    mem.Set(key, dto, TimeSpan.FromMinutes(5));
    return Results.Ok(new { source = "db", data = dto });
});

// --- HybridCache stampede demo: concurrent cold-cache requests → only 1 DB query ---
api.MapGet("/stampede/{id:guid}", async (
    Guid id,
    HttpContext http,
    HybridCache hybrid,
    CacheDbContext db,
    CacheQueryMetrics metrics,
    CancellationToken ct) =>
{
    var tenantId = TenantId(http);
    var dto = await hybrid.GetOrCreateAsync(
        $"tenant:{tenantId}:stampede:{id}",
        async token =>
        {
            metrics.Increment("stampede");
            // Simulate slow DB query
            await Task.Delay(100, token);
            var course = await db.Courses.FirstOrDefaultAsync(
                c => c.Id == id && c.CollegeId == tenantId,
                token);
            return course is null ? null : new CourseDto(course.Id, course.Code, course.Title, course.Credits, course.CollegeId);
        },
        tags: [$"tenant:{tenantId}:stampede"],
        cancellationToken: ct);

    return dto is null ? Results.NotFound() : Results.Ok(dto);
});

// --- Output Cache: sections list (named policy) ---
api.MapGet("/sections", () => Results.Ok(new { time = DateTimeOffset.UtcNow, demo = "Output Cache" }))
   .CacheOutput("Sections");

// --- Output Cache eviction ---
api.MapPost("/sections/invalidate", async (IOutputCacheStore store) =>
{
    await store.EvictByTagAsync("sections", default);
    return Results.Ok(new { evicted = true });
});

// --- Penetration: cache null result with short TTL ---
api.MapGet("/penetration/{id:guid}", async (
    Guid id,
    HttpContext http,
    HybridCache hybrid,
    CacheDbContext db,
    CacheQueryMetrics metrics,
    CancellationToken ct) =>
{
    var tenantId = TenantId(http);
    var dto = await hybrid.GetOrCreateAsync(
        $"tenant:{tenantId}:penetration:{id}",
        async token =>
        {
            metrics.Increment("penetration");
            var course = await db.Courses.FirstOrDefaultAsync(
                c => c.Id == id && c.CollegeId == tenantId,
                token);
            return course is null ? null : new CourseDto(course.Id, course.Code, course.Title, course.Credits, course.CollegeId);
        },
        options: new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromSeconds(30 + Random.Shared.Next(0, 10)),
            LocalCacheExpiration = TimeSpan.FromSeconds(10),
        },
        tags: [$"tenant:{tenantId}:penetration"],
        cancellationToken: ct);

    return dto is null ? Results.NotFound(new { demo = "null cached with short TTL, second call skips DB" }) : Results.Ok(dto);
});

app.Run();

static string TenantId(HttpContext http)
{
    var tenantId = http.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? "college-1";
    if (tenantId.Length is < 1 or > 64 || tenantId.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
    {
        throw new BadHttpRequestException("Invalid X-Tenant-Id.", StatusCodes.Status400BadRequest);
    }

    return tenantId;
}

public partial class Program;

public sealed record CreateCourseBody(string Code, string Title, int Credits, string? CollegeId = null);
public sealed record UpdateCourseBody(string Title, int Credits);
