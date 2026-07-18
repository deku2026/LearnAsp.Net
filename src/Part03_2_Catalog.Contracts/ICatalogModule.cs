namespace Part03_2.Catalog.Contracts;

/// <summary>Public API of Catalog module — other modules may only depend on this assembly.</summary>
public interface ICatalogModule
{
    Task<CourseDto> CreateCourseAsync(string code, string title, int credits, CancellationToken ct = default);
    Task<SectionDto> CreateSectionAsync(Guid courseId, string term, int capacity, CancellationToken ct = default);
    Task<SectionDto?> GetSectionAsync(Guid sectionId, CancellationToken ct = default);
    Task<bool> TryReserveSeatAsync(Guid sectionId, CancellationToken ct = default);
    Task ReleaseSeatAsync(Guid sectionId, CancellationToken ct = default);
}

public sealed record CourseDto(Guid Id, string Code, string Title, int Credits);
public sealed record SectionDto(Guid Id, Guid CourseId, string Term, int Capacity, int SeatsRemaining);
