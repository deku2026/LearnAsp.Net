using System.Collections.Concurrent;
using Campus.Contracts;

namespace Part03_1_ApiDesign;

public sealed class CampusStore
{
    private readonly ConcurrentDictionary<Guid, CourseEntity> _courses = new();
    private readonly ConcurrentDictionary<Guid, SectionEntity> _sections = new();
    private readonly ConcurrentDictionary<Guid, EnrollmentEntity> _enrollments = new();
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _idempotency = new(StringComparer.Ordinal);

    public CourseEntity AddCourse(string code, string title, int credits)
    {
        var e = new CourseEntity
        {
            Id = Guid.NewGuid(),
            Code = code.Trim(),
            Title = title.Trim(),
            Credits = credits,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = 1,
        };
        _courses[e.Id] = e;
        return e;
    }

    public CourseEntity? GetCourse(Guid id) => _courses.GetValueOrDefault(id);

    public IReadOnlyList<CourseEntity> ListCourses(string? q, string? after, int limit, out string? nextCursor)
    {
        IEnumerable<CourseEntity> qy = _courses.Values.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id);
        if (!string.IsNullOrWhiteSpace(q))
        {
            qy = qy.Where(c =>
                c.Code.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(after) && Guid.TryParse(after, out var afterId) &&
            _courses.TryGetValue(afterId, out var pivot))
        {
            qy = qy.Where(c =>
                c.CreatedAt > pivot.CreatedAt ||
                (c.CreatedAt == pivot.CreatedAt && c.Id.CompareTo(pivot.Id) > 0));
        }

        var page = qy.Take(Math.Clamp(limit, 1, 100) + 1).ToList();
        var hasMore = page.Count > limit;
        if (hasMore)
        {
            page = page.Take(limit).ToList();
        }

        nextCursor = hasMore ? page[^1].Id.ToString("N") : null;
        return page;
    }

    public CourseEntity? UpdateCourse(Guid id, string title, int credits, long ifMatch)
    {
        if (!_courses.TryGetValue(id, out var e))
        {
            return null;
        }

        if (e.RowVersion != ifMatch)
        {
            throw new ConcurrencyConflictException(e.RowVersion);
        }

        e.Title = title.Trim();
        e.Credits = credits;
        e.RowVersion++;
        return e;
    }

    public SectionEntity AddSection(Guid courseId, string term, int capacity)
    {
        if (!_courses.ContainsKey(courseId))
        {
            throw new KeyNotFoundException("course");
        }

        var e = new SectionEntity
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            Term = term.Trim(),
            Capacity = capacity,
            SeatsRemaining = capacity,
            RowVersion = 1,
        };
        _sections[e.Id] = e;
        return e;
    }

    public SectionEntity? GetSection(Guid id) => _sections.GetValueOrDefault(id);

    public EnrollmentEntity Enroll(Guid studentId, Guid sectionId, string? idempotencyKey, string bodyHash)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
            _idempotency.TryGetValue(idempotencyKey, out var existing))
        {
            if (!string.Equals(existing.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException();
            }

            return _enrollments[existing.EnrollmentId];
        }

        if (!_sections.TryGetValue(sectionId, out var section))
        {
            throw new KeyNotFoundException("section");
        }

        var dup = _enrollments.Values.Any(e =>
            e.StudentId == studentId &&
            e.SectionId == sectionId &&
            e.Status != EnrollmentStatus.Cancelled);
        if (dup)
        {
            throw new InvalidOperationException(ErrorCodes.EnrollmentDuplicate);
        }

        var status = section.SeatsRemaining > 0 ? EnrollmentStatus.Confirmed : EnrollmentStatus.Waitlisted;
        if (status == EnrollmentStatus.Confirmed)
        {
            section.SeatsRemaining--;
            section.RowVersion++;
        }

        var enrollment = new EnrollmentEntity
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            SectionId = sectionId,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = 1,
        };
        _enrollments[enrollment.Id] = enrollment;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _idempotency[idempotencyKey] = new IdempotencyRecord(enrollment.Id, bodyHash);
        }

        return enrollment;
    }

    public EnrollmentEntity? GetEnrollment(Guid id) => _enrollments.GetValueOrDefault(id);

    public IReadOnlyList<EnrollmentEntity> ListEnrollments(Guid? studentId) =>
        _enrollments.Values
            .Where(e => studentId is null || e.StudentId == studentId)
            .OrderByDescending(e => e.CreatedAt)
            .ToList();
}

public sealed class CourseEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int Credits { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long RowVersion { get; set; }
}

public sealed class SectionEntity
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string Term { get; set; } = "";
    public int Capacity { get; set; }
    public int SeatsRemaining { get; set; }
    public long RowVersion { get; set; }
}

public sealed class EnrollmentEntity
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public Guid SectionId { get; set; }
    public EnrollmentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long RowVersion { get; set; }
}

public sealed record IdempotencyRecord(Guid EnrollmentId, string BodyHash);

public sealed class ConcurrencyConflictException(long currentVersion) : Exception
{
    public long CurrentVersion { get; } = currentVersion;
}

public sealed class IdempotencyConflictException : Exception;
