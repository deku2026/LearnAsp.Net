namespace Part05_1_AuthnAuthz;

public sealed class W6SecurityOptions
{
    public string Authority { get; init; } = "http://localhost:8082/realms/campus-w6";

    public string Audience { get; init; } = "campus-api";

    public string WebClientId { get; init; } = "campus-web";

    public string? WebClientSecret { get; init; }

    public bool RequireHttpsMetadata { get; init; }

    public bool RequireSecureCookies { get; init; } = true;

    public bool UseRedis { get; init; } = true;

    public int ClockSkewSeconds { get; init; } = 30;

    public string[] AllowedOrigins { get; init; } = ["http://localhost:5173"];

    public int ApiTokenLimit { get; init; } = 20;

    public int ApiTokensPerPeriod { get; init; } = 10;

    public int ApiReplenishmentSeconds { get; init; } = 10;
}
