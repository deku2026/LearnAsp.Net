using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Part04_2_Caching;

public sealed class CacheDbContext(DbContextOptions<CacheDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>(ConfigureCourse);
    }

    private static void ConfigureCourse(EntityTypeBuilder<Course> e)
    {
        e.ToTable("courses");
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(32).IsRequired();
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Credits);
        e.Property(x => x.CollegeId).HasMaxLength(64).IsRequired();
        e.HasIndex(x => x.CollegeId);
    }
}

public sealed class Course
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Credits { get; set; }
    public string CollegeId { get; set; } = "college-1";
}

public sealed record CourseDto(Guid Id, string Code, string Title, int Credits, string CollegeId);
