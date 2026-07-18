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
    public void SetTenant(string collegeId) => CurrentCollegeId = collegeId;
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
                case EntityState.Modified when entry.Entity.CollegeId != currentTenant:
                    throw new InvalidOperationException(
                        $"禁止跨租户写入: entity belongs to {entry.Entity.CollegeId}, current tenant is {currentTenant}");
            }
        }
    }
}

// Tenant resolution middleware
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantSetter setter)
    {
        // Priority: JWT claim → X-Tenant-Id header → default
        var tenantId = context.User.FindFirst("college_id")?.Value
                       ?? context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                       ?? "college-1";

        setter.SetTenant(tenantId);
        await next(context);
    }
}

public sealed record CourseDto(Guid Id, string Code, string Title, int Credits, string CollegeId);
public sealed record CreateCourseBody(string Code, string Title, int Credits);