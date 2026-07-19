using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Part04_3_MultiTenant;

// Tenant context — scoped, set per-request by middleware
public interface ITenantContext
{
    string? CurrentCollegeId { get; }
}

public interface ITenantSetter
{
    void SetTenant(string collegeId);
}

public sealed class TenantContext : ITenantContext, ITenantSetter
{
    public string? CurrentCollegeId { get; private set; }

    public void SetTenant(string collegeId)
    {
        if (CurrentCollegeId is not null &&
            !string.Equals(CurrentCollegeId, collegeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Tenant context is immutable for the lifetime of a request.");
        }

        CurrentCollegeId = collegeId;
    }
}

// Entities implementing ITenantEntity
public interface ITenantEntity
{
    string CollegeId { get; set; }
}

public sealed class Course : ITenantEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Credits { get; set; }
    public string CollegeId { get; set; } = "college-1";
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// DbContext with named query filters + SaveChanges write protection
public sealed class TenantDbContext(
    DbContextOptions<TenantDbContext> options,
    ITenantContext tenant) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>(ConfigureCourse);
    }

    private void ConfigureCourse(EntityTypeBuilder<Course> e)
    {
        e.ToTable("courses");
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(32).IsRequired();
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Credits);
        e.Property(x => x.CollegeId).HasMaxLength(64).IsRequired();
        e.Property(x => x.IsDeleted);
        e.Property(x => x.CreatedAt);
        e.HasIndex(x => x.CollegeId);

        // EF10 named query filters: tenant + soft-delete independently togglable
        e.HasQueryFilter("Tenant", c => c.CollegeId == tenant.CurrentCollegeId);
        e.HasQueryFilter("SoftDelete", c => !c.IsDeleted);
    }

    // Write protection: prevent cross-tenant writes
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectCrossTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ProtectCrossTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ProtectCrossTenantWrites()
    {
        var currentTenant = tenant.CurrentCollegeId
            ?? throw new InvalidOperationException("No tenant context set");

        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Force stamp with current tenant (even if caller forgot to set it)
                    entry.Entity.CollegeId = currentTenant;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    var originalTenant = entry.Property(nameof(ITenantEntity.CollegeId)).OriginalValue as string;
                    if (entry.Entity.CollegeId == currentTenant &&
                        string.Equals(originalTenant, currentTenant, StringComparison.Ordinal))
                    {
                        break;
                    }

                    throw new InvalidOperationException(
                        $"禁止跨租户写入: entity belongs to {originalTenant ?? entry.Entity.CollegeId}, current tenant is {currentTenant}");
            }
        }
    }
}

// Tenant resolution middleware
public sealed class TenantResolutionMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private readonly HashSet<string> _allowedTenants = configuration
        .GetSection("Tenants:Allowed")
        .Get<string[]>()?
        .ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(["college-1"], StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context, ITenantSetter setter)
    {
        // Priority: JWT claim → X-Tenant-Id header → default
        var tenantId = context.User.FindFirst("college_id")?.Value
                       ?? context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                       ?? "college-1";

        if (tenantId.Length is < 1 or > 64 ||
            tenantId.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')) ||
            !_allowedTenants.Contains(tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { errorCode = "tenant.not_found" });
            return;
        }

        setter.SetTenant(tenantId);
        await next(context);
    }
}

public sealed record CourseDto(Guid Id, string Code, string Title, int Credits, string CollegeId);
public sealed record CreateCourseBody(string Code, string Title, int Credits);

public static class TenantCacheKey
{
    public static string Course(string tenantId, Guid courseId) => $"tenant:{tenantId}:course:{courseId:N}";

    public static string CoursesTag(string tenantId) => $"tenant:{tenantId}:courses";
}
