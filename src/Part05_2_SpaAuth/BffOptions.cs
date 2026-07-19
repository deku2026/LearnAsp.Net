namespace Part05_2_SpaAuth;

public sealed class BffOptions
{
    public string Authority { get; init; } = "http://localhost:8082/realms/campus-w6";

    public string ClientId { get; init; } = "campus-bff";

    public string? ClientSecret { get; init; }

    public string DownstreamApi { get; init; } = "http://localhost:5018";

    public string? PublicOrigin { get; init; }

    public bool RequireHttpsMetadata { get; init; }

    public bool RequireSecureCookies { get; init; } = true;

    public bool UseRedis { get; init; } = true;

    public int RefreshBeforeExpirySeconds { get; init; } = 60;
}
