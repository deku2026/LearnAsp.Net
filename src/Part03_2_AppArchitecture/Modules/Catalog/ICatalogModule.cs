namespace Part03_2_AppArchitecture.Modules.Catalog;

public interface ICatalogModule
{
    Task<SectionInfo?> GetSectionAsync(Guid sectionId, CancellationToken ct = default);
    Task<bool> TryReserveSeatAsync(Guid sectionId, CancellationToken ct = default);
    Task ReleaseSeatAsync(Guid sectionId, CancellationToken ct = default);
    Task<CourseInfo> CreateCourseAsync(string code, string title, int credits, CancellationToken ct = default);
    Task<SectionInfo> CreateSectionAsync(Guid courseId, string term, int capacity, CancellationToken ct = default);
}

public sealed record CourseInfo(Guid Id, string Code, string Title, int Credits);
public sealed record SectionInfo(Guid Id, Guid CourseId, string Term, int Capacity, int SeatsRemaining);
