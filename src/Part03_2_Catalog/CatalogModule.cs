using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Part03_2.Catalog.Contracts;

namespace Part03_2.Catalog;

public sealed class CatalogModule(CatalogDbContext db) : ICatalogModule
{
    public async Task<CourseDto> CreateCourseAsync(string code, string title, int credits, CancellationToken ct = default)
    {
        var row = new CourseRow
        {
            Id = Guid.NewGuid(),
            Code = code.Trim(),
            Title = title.Trim(),
            Credits = credits,
        };
        db.Courses.Add(row);
        await db.SaveChangesAsync(ct);
        return new CourseDto(row.Id, row.Code, row.Title, row.Credits);
    }

    public async Task<SectionDto> CreateSectionAsync(Guid courseId, string term, int capacity, CancellationToken ct = default)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == courseId, ct))
        {
            throw new KeyNotFoundException("course");
        }

        var row = new SectionRow
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            Term = term.Trim(),
            Capacity = capacity,
            SeatsRemaining = capacity,
        };
        db.Sections.Add(row);
        await db.SaveChangesAsync(ct);
        return new SectionDto(row.Id, row.CourseId, row.Term, row.Capacity, row.SeatsRemaining);
    }

    public async Task<SectionDto?> GetSectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        var row = await db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectionId, ct);
        return row is null ? null : new SectionDto(row.Id, row.CourseId, row.Term, row.Capacity, row.SeatsRemaining);
    }

    public async Task<bool> TryReserveSeatAsync(Guid sectionId, CancellationToken ct = default)
    {
        var row = await db.Sections.FirstOrDefaultAsync(s => s.Id == sectionId, ct);
        if (row is null || row.SeatsRemaining <= 0)
        {
            return false;
        }

        row.SeatsRemaining--;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReleaseSeatAsync(Guid sectionId, CancellationToken ct = default)
    {
        var row = await db.Sections.FirstOrDefaultAsync(s => s.Id == sectionId, ct);
        if (row is not null && row.SeatsRemaining < row.Capacity)
        {
            row.SeatsRemaining++;
            await db.SaveChangesAsync(ct);
        }
    }
}

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<CourseRow> Courses => Set<CourseRow>();
    public DbSet<SectionRow> Sections => Set<SectionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Separate InMemory database name provides module isolation; relational schemas come with Npgsql in Part04.
        modelBuilder.Entity<CourseRow>().HasKey(x => x.Id);
        modelBuilder.Entity<SectionRow>().HasKey(x => x.Id);
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

public static class CatalogModuleExtensions
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddDbContext<CatalogDbContext>(o => o.UseInMemoryDatabase("part03_2_catalog"));
        services.AddScoped<ICatalogModule, CatalogModule>();
        return services;
    }
}
