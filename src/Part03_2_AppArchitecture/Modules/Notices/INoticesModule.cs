namespace Part03_2_AppArchitecture.Modules.Notices;

public interface INoticesModule
{
    Task HandleEnrollmentConfirmedAsync(Guid enrollmentId, Guid studentId, Guid sectionId, CancellationToken ct = default);
    Task<IReadOnlyList<NoticeInfo>> ListAsync(CancellationToken ct = default);
}

public sealed record NoticeInfo(Guid Id, Guid StudentId, string Title, string Body, DateTimeOffset CreatedAt);
