using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Part03_2.BuildingBlocks;
using Part03_2.Enrollment.Contracts;
using Part03_2.Notices.Contracts;

namespace Part03_2.Notices;

public sealed class NoticesModule(NoticesDbContext db) : INoticesModule, IOutboxMessageHandler
{
    public string MessageType => nameof(EnrollmentConfirmed);

    public async Task HandleAsync(string payloadJson, CancellationToken ct = default)
    {
        var evt = JsonSerializer.Deserialize<EnrollmentConfirmed>(payloadJson)
                  ?? throw new InvalidOperationException("invalid EnrollmentConfirmed payload");

        // Idempotent consumer: one notice per enrollment
        if (await db.Notices.AnyAsync(n => n.EnrollmentId == evt.EnrollmentId, ct))
        {
            return;
        }

        db.Notices.Add(new NoticeRow
        {
            Id = Guid.NewGuid(),
            EnrollmentId = evt.EnrollmentId,
            StudentId = evt.StudentId,
            Title = "Enrollment confirmed",
            Body = $"Enrollment {evt.EnrollmentId} for section {evt.SectionId} is confirmed.",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NoticeDto>> ListAsync(CancellationToken ct = default)
        => await db.Notices.AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoticeDto(n.Id, n.StudentId, n.Title, n.Body, n.CreatedAt))
            .ToListAsync(ct);
}

public sealed class NoticesDbContext(DbContextOptions<NoticesDbContext> options) : DbContext(options)
{
    public DbSet<NoticeRow> Notices => Set<NoticeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NoticeRow>().HasKey(x => x.Id);
        modelBuilder.Entity<NoticeRow>().HasIndex(n => n.EnrollmentId).IsUnique();
    }
}

public sealed class NoticeRow
{
    public Guid Id { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid StudentId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public static class NoticesModuleExtensions
{
    public static IServiceCollection AddNoticesModule(this IServiceCollection services)
    {
        services.AddDbContext<NoticesDbContext>(o => o.UseInMemoryDatabase("part03_2_notices"));
        services.AddScoped<NoticesModule>();
        services.AddScoped<INoticesModule>(sp => sp.GetRequiredService<NoticesModule>());
        services.AddScoped<IOutboxMessageHandler>(sp => sp.GetRequiredService<NoticesModule>());
        return services;
    }
}
