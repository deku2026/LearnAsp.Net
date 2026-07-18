using System.Collections.Concurrent;
using Campus.Contracts;
using Part03_2_AppArchitecture.Modules.Catalog;
using Part03_2_AppArchitecture.Outbox;

namespace Part03_2_AppArchitecture.Modules.Enrollment;

public sealed class EnrollmentModule(ICatalogModule catalog, IOutbox outbox) : IEnrollmentModule
{
    private readonly ConcurrentDictionary<Guid, EnrollmentInfo> _items = new();

    public async Task<EnrollmentInfo> EnrollAsync(Guid studentId, Guid sectionId, CancellationToken ct = default)
    {
        var section = await catalog.GetSectionAsync(sectionId, ct)
                      ?? throw new KeyNotFoundException("section");

        var reserved = await catalog.TryReserveSeatAsync(sectionId, ct);
        var status = reserved ? EnrollmentStatus.Confirmed : EnrollmentStatus.Waitlisted;

        var info = new EnrollmentInfo(Guid.NewGuid(), studentId, sectionId, status, DateTimeOffset.UtcNow);
        _items[info.Id] = info;

        if (status == EnrollmentStatus.Confirmed)
        {
            await outbox.EnqueueAsync(
                nameof(EnrollmentConfirmed),
                new EnrollmentConfirmed(info.Id, info.StudentId, info.SectionId, DateTimeOffset.UtcNow),
                ct);
        }

        _ = section; // capacity checked via reserve
        return info;
    }

    public Task<IReadOnlyList<EnrollmentInfo>> ListAsync(Guid? studentId = null, CancellationToken ct = default)
    {
        IEnumerable<EnrollmentInfo> q = _items.Values;
        if (studentId is not null)
        {
            q = q.Where(e => e.StudentId == studentId);
        }

        return Task.FromResult<IReadOnlyList<EnrollmentInfo>>(q.OrderByDescending(e => e.CreatedAt).ToList());
    }
}
