using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Part03_2.BuildingBlocks;
using Part03_2.Catalog.Contracts;
using Part03_2.Enrollment.Contracts;

namespace Part03_2.Enrollment;

public sealed class EnrollmentModule(EnrollmentDbContext db, ICatalogModule catalog, IOutbox outbox) : IEnrollmentModule
{
    public async Task<EnrollmentDto> EnrollAsync(Guid studentId, Guid sectionId, CancellationToken ct = default)
    {
        _ = await catalog.GetSectionAsync(sectionId, ct) ?? throw new KeyNotFoundException("section");

        var reserved = await catalog.TryReserveSeatAsync(sectionId, ct);
        var aggregate = EnrollmentAggregate.Create(studentId, sectionId, reserved);

        db.Enrollments.Add(new EnrollmentRow
        {
            Id = aggregate.Id,
            StudentId = aggregate.StudentId,
            SectionId = aggregate.SectionId,
            Status = aggregate.Status,
            CreatedAt = aggregate.CreatedAt,
        });
        await db.SaveChangesAsync(ct);

        if (aggregate.IsConfirmed)
        {
            await outbox.EnqueueAsync(
                nameof(EnrollmentConfirmed),
                new EnrollmentConfirmed(aggregate.Id, aggregate.StudentId, aggregate.SectionId, DateTimeOffset.UtcNow),
                ct);
        }

        return new EnrollmentDto(aggregate.Id, aggregate.StudentId, aggregate.SectionId, aggregate.Status, aggregate.CreatedAt);
    }

    public async Task<IReadOnlyList<EnrollmentDto>> ListAsync(Guid? studentId = null, CancellationToken ct = default)
    {
        var q = db.Enrollments.AsNoTracking().AsQueryable();
        if (studentId is not null)
        {
            q = q.Where(e => e.StudentId == studentId);
        }

        return await q.OrderByDescending(e => e.CreatedAt)
            .Select(e => new EnrollmentDto(e.Id, e.StudentId, e.SectionId, e.Status, e.CreatedAt))
            .ToListAsync(ct);
    }
}

public sealed class EnrollmentDbContext(DbContextOptions<EnrollmentDbContext> options) : DbContext(options)
{
    public DbSet<EnrollmentRow> Enrollments => Set<EnrollmentRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EnrollmentRow>().HasKey(x => x.Id);
    }
}

public sealed class EnrollmentRow
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public Guid SectionId { get; set; }
    public Campus.Contracts.EnrollmentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class EnrollmentModuleExtensions
{
    public static IServiceCollection AddEnrollmentModule(this IServiceCollection services)
    {
        services.AddDbContext<EnrollmentDbContext>(o => o.UseInMemoryDatabase("part03_2_enrollment"));
        services.AddScoped<IEnrollmentModule, EnrollmentModule>();
        return services;
    }
}
