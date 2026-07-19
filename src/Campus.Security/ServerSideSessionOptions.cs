namespace Campus.Security;

public sealed class ServerSideSessionOptions
{
    public required string CookieScheme { get; init; }

    public required string ApplicationName { get; init; }

    public string KeyPrefix { get; init; } = "Campus:Security:";

    public bool UseRedis { get; init; } = true;

    public TimeSpan DefaultLifetime { get; init; } = TimeSpan.FromHours(8);
}
