using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Campus.Security;

/// <summary>
/// Keeps the complete authentication ticket, including OIDC tokens, on the server.
/// The browser cookie contains only the opaque key returned by this store.
/// </summary>
public sealed class ServerSideTicketStore(
    IDistributedCache cache,
    IOptions<ServerSideSessionOptions> options,
    TimeProvider timeProvider) : ITicketStore
{
    private readonly ServerSideSessionOptions _options = options.Value;

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreCoreAsync(ticket, CancellationToken.None);

    public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        StoreCoreAsync(ticket, cancellationToken);

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        StoreCoreAsync(ticket, cancellationToken);

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewCoreAsync(key, ticket, CancellationToken.None);

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken) =>
        RenewCoreAsync(key, ticket, cancellationToken);

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        RenewCoreAsync(key, ticket, cancellationToken);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveCoreAsync(key, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken) =>
        RetrieveCoreAsync(key, cancellationToken);

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        RetrieveCoreAsync(key, cancellationToken);

    public Task RemoveAsync(string key) =>
        cache.RemoveAsync(CacheKey(key));

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        cache.RemoveAsync(CacheKey(key), cancellationToken);

    public Task RemoveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        cache.RemoveAsync(CacheKey(key), cancellationToken);

    private async Task<string> StoreCoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        var key = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await RenewCoreAsync(key, ticket, cancellationToken);
        return key;
    }

    private Task RenewCoreAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expires = ticket.Properties.ExpiresUtc;
        var lifetime = expires is not null && expires > now
            ? expires.Value - now
            : _options.DefaultLifetime;

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime,
        };

        return cache.SetAsync(
            CacheKey(key),
            TicketSerializer.Default.Serialize(ticket),
            cacheOptions,
            cancellationToken);
    }

    private async Task<AuthenticationTicket?> RetrieveCoreAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var bytes = await cache.GetAsync(CacheKey(key), cancellationToken);
        return bytes is null ? null : TicketSerializer.Default.Deserialize(bytes);
    }

    private string CacheKey(string key) => $"{_options.KeyPrefix}ticket:{key}";
}
