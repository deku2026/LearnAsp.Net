using Campus.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Part04_1_EFCore;

public sealed class CampusDbContext(DbContextOptions<CampusDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>(ConfigureCourse);
        modelBuilder.Entity<Section>(ConfigureSection);
        modelBuilder.Entity<Enrollment>(ConfigureEnrollment);
        modelBuilder.Entity<AttendanceRecord>(ConfigureAttendance);
    }

    private static void ConfigureCourse(EntityTypeBuilder<Course> e)
    {
        e.ToTable("courses");
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(32).IsRequired();
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Credits);
        e.Property(x => x.CollegeId).HasMaxLength(64).IsRequired();
        e.Property(x => x.IsDeleted);
        e.Property(x => x.CreatedAt);
        // PostgreSQL xmin as optimistic concurrency token (manual config for Npgsql)
        e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
        // Composite index for keyset pagination (CreatedAt, Id)
        e.HasIndex(x => new { x.CreatedAt, x.Id });
        // Soft-delete + tenant named query filters (EF10)
        e.HasQueryFilter("SoftDelete", c => !c.IsDeleted);
        e.HasQueryFilter("Tenant", c => c.CollegeId == EfTenantAccessor.CurrentCollegeId);
    }

    private static void ConfigureSection(EntityTypeBuilder<Section> e)
    {
        e.ToTable("sections");
        e.HasKey(x => x.Id);
        e.Property(x => x.SectionName).HasMaxLength(100).IsRequired();
        e.Property(x => x.Semester).HasMaxLength(32).IsRequired();
        e.Property(x => x.Capacity);
        e.Property(x => x.Status).HasMaxLength(20).IsRequired();
        e.Property(x => x.CollegeId).HasMaxLength(64).IsRequired();
        e.Property(x => x.IsDeleted);
        e.Property(x => x.CreatedAt);
        e.HasOne(x => x.Course).WithMany(c => c.Sections).HasForeignKey(x => x.CourseId);
        // Composite index for keyset pagination
        e.HasIndex(x => new { x.CreatedAt, x.Id });
        e.HasIndex(x => x.CourseId);
        e.HasQueryFilter("SoftDelete", s => !s.IsDeleted);
        e.HasQueryFilter("Tenant", s => s.CollegeId == EfTenantAccessor.CurrentCollegeId);
    }

    private static void ConfigureEnrollment(EntityTypeBuilder<Enrollment> e)
    {
        e.ToTable("enrollments");
        e.HasKey(x => x.Id);
        e.Property(x => x.StudentName).HasMaxLength(100).IsRequired();
        e.Property(x => x.Grade).HasMaxLength(2);
        e.Property(x => x.CollegeId).HasMaxLength(64).IsRequired();
        e.Property(x => x.IsDeleted);
        e.Property(x => x.EnrolledAt);
        e.HasOne(x => x.Section).WithMany(s => s.Enrollments).HasForeignKey(x => x.SectionId);
        e.HasIndex(x => x.SectionId);
        e.HasQueryFilter("SoftDelete", x => !x.IsDeleted);
        e.HasQueryFilter("Tenant", x => x.CollegeId == EfTenantAccessor.CurrentCollegeId);
    }

    private static void ConfigureAttendance(EntityTypeBuilder<AttendanceRecord> e)
    {
        e.ToTable("attendance_records");
        e.HasKey(x => x.Id);
        e.Property(x => x.Date);
        e.Property(x => x.Present);
        e.HasOne(x => x.Enrollment).WithMany(en => en.AttendanceRecords).HasForeignKey(x => x.EnrollmentId);
        e.HasIndex(x => x.EnrollmentId);
    }
}

// Entities

public sealed class Course
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Credits { get; set; }
    public string CollegeId { get; set; } = "college-1";
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Section> Sections { get; set; } = [];
}

public sealed class Section
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public Course? Course { get; set; }
    public string SectionName { get; set; } = "";
    public string Semester { get; set; } = "";
    public int Capacity { get; set; }
    public string Status { get; set; } = "Open";
    public string CollegeId { get; set; } = "college-1";
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Enrollment> Enrollments { get; set; } = [];
}

public sealed class Enrollment
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }
    public Section? Section { get; set; }
    public string StudentName { get; set; } = "";
    public string? Grade { get; set; }
    public string CollegeId { get; set; } = "college-1";
    public bool IsDeleted { get; set; }
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = [];
}

public sealed class AttendanceRecord
{
    public Guid Id { get; set; }
    public Guid EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }
    public DateOnly Date { get; set; }
    public bool Present { get; set; }
}

/// <summary>Ambient tenant accessor for EF query filters (set per-request by middleware).</summary>
public static class EfTenantAccessor
{
    private static readonly AsyncLocal<string?> _current = new();
    public static string CurrentCollegeId => _current.Value ?? "college-1";
    public static void SetTenant(string collegeId) => _current.Value = collegeId;
    public static void Clear() => _current.Value = null;
}

// DTOs for projection demo

public sealed record SectionListItemDto(
    Guid Id,
    string CourseCode,
    string CourseTitle,
    string SectionName,
    string Semester,
    int Capacity,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CourseDetailDto(
    Guid Id,
    string Code,
    string Title,
    int Credits,
    string CollegeId,
    DateTimeOffset CreatedAt);