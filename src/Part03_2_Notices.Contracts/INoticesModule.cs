namespace Part03_2.Notices.Contracts;

public interface INoticesModule
{
    Task<IReadOnlyList<NoticeDto>> ListAsync(CancellationToken ct = default);
}

public sealed record NoticeDto(Guid Id, Guid StudentId, string Title, string Body, DateTimeOffset CreatedAt);
