using System.Collections.Concurrent;
using Campus.Contracts;

namespace Step05_MinimalApiVsController;

public sealed class CampusStore
{
    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<Guid, CourseDto> _courses = new();
    private readonly ConcurrentDictionary<Guid, SectionDto> _sections = new();
    private readonly ConcurrentDictionary<Guid, EnrollmentDto> _enrollments = new();

    public CourseDto AddCourse(CreateCourseRequest request)
    {
        var dto = new CourseDto(Guid.NewGuid(), request.Code.Trim(), request.Title.Trim(), request.Credits);
        _courses[dto.Id] = dto;
        return dto;
    }

    public IReadOnlyList<CourseDto> ListCourses(string? q = null)
    {
        IEnumerable<CourseDto> query = _courses.Values;
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(c =>
                c.Code.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return query.OrderBy(c => c.Code).ToList();
    }

    public CourseDto? GetCourse(Guid id) => _courses.GetValueOrDefault(id);

    public CourseDto? FindByCode(string code) =>
        _courses.Values.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public CourseDto? UpdateCourse(Guid id, CreateCourseRequest request)
    {
        lock (_stateLock)
        {
            if (!_courses.ContainsKey(id))
            {
                return null;
            }

            var updated = new CourseDto(id, request.Code.Trim(), request.Title.Trim(), request.Credits);
            _courses[id] = updated;
            return updated;
        }
    }

    public bool DeleteCourse(Guid id)
    {
        lock (_stateLock)
        {
            if (_sections.Values.Any(section => section.CourseId == id))
            {
                return false;
            }

            return _courses.TryRemove(id, out _);
        }
    }

    public SectionDto AddSection(CreateSectionRequest request)
    {
        if (!_courses.ContainsKey(request.CourseId))
        {
            throw new KeyNotFoundException("course");
        }

        var dto = new SectionDto(Guid.NewGuid(), request.CourseId, request.Term.Trim(), request.Capacity, request.Capacity);
        _sections[dto.Id] = dto;
        return dto;
    }

    public IReadOnlyList<SectionDto> ListSections() => _sections.Values.OrderBy(s => s.Term).ToList();

    public SectionDto? GetSection(Guid id) => _sections.GetValueOrDefault(id);

    public EnrollmentDto Enroll(CreateEnrollmentRequest request)
    {
        lock (_stateLock)
        {
            if (!_sections.TryGetValue(request.SectionId, out var section))
            {
                throw new KeyNotFoundException("section");
            }

            var duplicate = _enrollments.Values.Any(e =>
                e.StudentId == request.StudentId &&
                e.SectionId == request.SectionId &&
                e.Status is not EnrollmentStatus.Cancelled);

            if (duplicate)
            {
                throw new InvalidOperationException(ErrorCodes.EnrollmentDuplicate);
            }

            EnrollmentStatus status;
            if (section.SeatsRemaining > 0)
            {
                status = EnrollmentStatus.Confirmed;
                var updated = section with { SeatsRemaining = section.SeatsRemaining - 1 };
                _sections[section.Id] = updated;
            }
            else
            {
                status = EnrollmentStatus.Waitlisted;
            }

            var enrollment = new EnrollmentDto(
                Guid.NewGuid(),
                request.StudentId,
                request.SectionId,
                status,
                DateTimeOffset.UtcNow);
            _enrollments[enrollment.Id] = enrollment;
            return enrollment;
        }
    }

    public EnrollmentDto? GetEnrollment(Guid id) => _enrollments.GetValueOrDefault(id);

    public IReadOnlyList<EnrollmentDto> ListEnrollments(Guid? studentId = null)
    {
        IEnumerable<EnrollmentDto> q = _enrollments.Values;
        if (studentId is not null)
        {
            q = q.Where(e => e.StudentId == studentId);
        }

        return q.OrderByDescending(e => e.CreatedAt).ToList();
    }

    public EnrollmentDto Cancel(Guid id)
    {
        lock (_stateLock)
        {
            if (!_enrollments.TryGetValue(id, out var enrollment))
            {
                throw new KeyNotFoundException("enrollment");
            }

            if (enrollment.Status == EnrollmentStatus.Cancelled)
            {
                return enrollment;
            }

            if (enrollment.Status == EnrollmentStatus.Confirmed &&
                _sections.TryGetValue(enrollment.SectionId, out var section))
            {
                var nextWaitlisted = _enrollments.Values
                    .Where(candidate =>
                        candidate.SectionId == enrollment.SectionId &&
                        candidate.Status == EnrollmentStatus.Waitlisted)
                    .OrderBy(candidate => candidate.CreatedAt)
                    .FirstOrDefault();

                if (nextWaitlisted is null)
                {
                    _sections[section.Id] = section with { SeatsRemaining = section.SeatsRemaining + 1 };
                }
                else
                {
                    _enrollments[nextWaitlisted.Id] = nextWaitlisted with { Status = EnrollmentStatus.Confirmed };
                }
            }

            var cancelled = enrollment with { Status = EnrollmentStatus.Cancelled };
            _enrollments[id] = cancelled;
            return cancelled;
        }
    }
}
