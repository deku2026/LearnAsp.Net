using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Campus.Security;

public sealed class SessionRevocationStore(
    IDistributedCache cache,
    IOptions<ServerSideSessionOptions> options)
{
    private static readonly byte[] RevokedMarker = [1];
    private readonly ServerSideSessionOptions _options = options.Value;

    public async Task RevokeAsync(
        string? sessionId,
        string? subject,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = expiresAt,
        };

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await cache.SetAsync(
                RevocationKey("sid", sessionId),
                RevokedMarker,
                entryOptions,
                cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(subject))
        {
            await cache.SetAsync(
                RevocationKey("sub", subject),
                RevokedMarker,
                entryOptions,
                cancellationToken);
        }
    }

    public async Task<bool> IsRevokedAsync(
        string? sessionId,
        string? subject,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionId) &&
            await cache.GetAsync(RevocationKey("sid", sessionId), cancellationToken) is not null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(subject) &&
               await cache.GetAsync(RevocationKey("sub", subject), cancellationToken) is not null;
    }

    private string RevocationKey(string kind, string value) =>
        $"{_options.KeyPrefix}revoked:{kind}:{value}";
}
