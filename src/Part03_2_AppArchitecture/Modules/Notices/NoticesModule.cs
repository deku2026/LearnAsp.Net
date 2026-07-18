using System.Collections.Concurrent;

namespace Part03_2_AppArchitecture.Modules.Notices;

public sealed class NoticesModule : INoticesModule
{
    private readonly ConcurrentDictionary<Guid, NoticeInfo> _notices = new();
    private readonly ConcurrentDictionary<Guid, byte> _processedEnrollments = new();

    public Task HandleEnrollmentConfirmedAsync(Guid enrollmentId, Guid studentId, Guid sectionId, CancellationToken ct = default)
    {
        // Idempotent consumer
        if (!_processedEnrollments.TryAdd(enrollmentId, 0))
        {
            return Task.CompletedTask;
        }

        var n = new NoticeInfo(
            Guid.NewGuid(),
            studentId,
            "Enrollment confirmed",
            $"Enrollment {enrollmentId} for section {sectionId} is confirmed.",
            DateTimeOffset.UtcNow);
        _notices[n.Id] = n;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NoticeInfo>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<NoticeInfo>>(_notices.Values.OrderByDescending(n => n.CreatedAt).ToList());
}
