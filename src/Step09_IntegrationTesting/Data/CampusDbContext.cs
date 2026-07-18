using Microsoft.EntityFrameworkCore;

namespace Step09_IntegrationTesting.Data;

public sealed class CampusDbContext(DbContextOptions<CampusDbContext> options) : DbContext(options)
{
    public DbSet<CourseRow> Courses => Set<CourseRow>();
    public DbSet<SectionRow> Sections => Set<SectionRow>();
    public DbSet<EnrollmentRow> Enrollments => Set<EnrollmentRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CourseRow>(e =>
        {
            e.ToTable("courses");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<SectionRow>(e =>
        {
            e.ToTable("sections");
            e.HasKey(x => x.Id);
            e.HasOne<CourseRow>().WithMany().HasForeignKey(x => x.CourseId);
            e.Property(x => x.Term).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<EnrollmentRow>(e =>
        {
            e.ToTable("enrollments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.StudentId, x.SectionId });
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });
    }
}

public sealed class CourseRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Credits { get; set; }
}

public sealed class SectionRow
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string Term { get; set; } = "";
    public int Capacity { get; set; }
    public int SeatsRemaining { get; set; }
}

public sealed class EnrollmentRow
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public Guid SectionId { get; set; }
    public string Status { get; set; } = "Confirmed";
    public DateTimeOffset CreatedAt { get; set; }
}
