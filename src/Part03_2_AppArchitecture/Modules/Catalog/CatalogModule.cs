using System.Collections.Concurrent;

namespace Part03_2_AppArchitecture.Modules.Catalog;

public sealed class CatalogModule : ICatalogModule
{
    private readonly ConcurrentDictionary<Guid, CourseInfo> _courses = new();
    private readonly ConcurrentDictionary<Guid, MutableSection> _sections = new();

    public Task<CourseInfo> CreateCourseAsync(string code, string title, int credits, CancellationToken ct = default)
    {
        var c = new CourseInfo(Guid.NewGuid(), code.Trim(), title.Trim(), credits);
        _courses[c.Id] = c;
        return Task.FromResult(c);
    }

    public Task<SectionInfo> CreateSectionAsync(Guid courseId, string term, int capacity, CancellationToken ct = default)
    {
        if (!_courses.ContainsKey(courseId))
        {
            throw new KeyNotFoundException("course");
        }

        var s = new MutableSection(Guid.NewGuid(), courseId, term.Trim(), capacity, capacity);
        _sections[s.Id] = s;
        return Task.FromResult(s.ToInfo());
    }

    public Task<SectionInfo?> GetSectionAsync(Guid sectionId, CancellationToken ct = default)
        => Task.FromResult(_sections.TryGetValue(sectionId, out var s) ? s.ToInfo() : null);

    public Task<bool> TryReserveSeatAsync(Guid sectionId, CancellationToken ct = default)
    {
        if (!_sections.TryGetValue(sectionId, out var s) || s.SeatsRemaining <= 0)
        {
            return Task.FromResult(false);
        }

        s.SeatsRemaining--;
        return Task.FromResult(true);
    }

    public Task ReleaseSeatAsync(Guid sectionId, CancellationToken ct = default)
    {
        if (_sections.TryGetValue(sectionId, out var s) && s.SeatsRemaining < s.Capacity)
        {
            s.SeatsRemaining++;
        }

        return Task.CompletedTask;
    }

    private sealed class MutableSection(Guid id, Guid courseId, string term, int capacity, int seats)
    {
        public Guid Id { get; } = id;
        public Guid CourseId { get; } = courseId;
        public string Term { get; } = term;
        public int Capacity { get; } = capacity;
        public int SeatsRemaining { get; set; } = seats;
        public SectionInfo ToInfo() => new(Id, CourseId, Term, Capacity, SeatsRemaining);
    }
}
