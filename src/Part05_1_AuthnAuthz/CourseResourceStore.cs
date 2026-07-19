using System.Collections.Concurrent;

namespace Part05_1_AuthnAuthz;

public sealed record CourseResource(
    Guid Id,
    string Code,
    string Title,
    string OwnerSubject,
    DateTimeOffset CreatedAt) : IOwnedResource;

public sealed class CourseResourceStore
{
    private readonly ConcurrentDictionary<Guid, CourseResource> _courses = new();

    public IReadOnlyCollection<CourseResource> ListFor(string subject, bool includeAll) =>
        _courses.Values
            .Where(course => includeAll ||
                             string.Equals(course.OwnerSubject, subject, StringComparison.Ordinal))
            .OrderBy(course => course.Code, StringComparer.Ordinal)
            .ToArray();

    public CourseResource? Find(Guid id) =>
        _courses.GetValueOrDefault(id);

    public CourseResource Create(string code, string title, string ownerSubject)
    {
        var course = new CourseResource(
            Guid.NewGuid(),
            code.Trim(),
            title.Trim(),
            ownerSubject,
            DateTimeOffset.UtcNow);
        return _courses.TryAdd(course.Id, course)
            ? course
            : throw new InvalidOperationException("Could not create a unique course resource.");
    }

    public CourseResource? Update(Guid id, string title)
    {
        while (_courses.TryGetValue(id, out var current))
        {
            var updated = current with { Title = title.Trim() };
            if (_courses.TryUpdate(id, updated, current))
            {
                return updated;
            }
        }

        return null;
    }

    public bool Remove(Guid id) => _courses.TryRemove(id, out _);
}

public sealed record CreateCourseRequest(string Code, string Title);

public sealed record UpdateCourseRequest(string Title);
