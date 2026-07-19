using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Campus.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Part05_1_AuthnAuthz;

public static class SecurityConfigurationExtensions
{
    public static IServiceCollection AddW6Security(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection("Security").Get<W6SecurityOptions>()
            ?? new W6SecurityOptions();
        ValidateOptions(options);
        services.AddSingleton(Options.Create(options));

        services.AddCampusServerSideSessions(
            configuration,
            new ServerSideSessionOptions
            {
                CookieScheme = SecuritySchemes.WebCookie,
                ApplicationName = "Campus.Part05_1",
                KeyPrefix = "Campus:Part05_1:",
                UseRedis = options.UseRedis,
            });

        services
            .AddAuthentication(authentication =>
            {
                authentication.DefaultAuthenticateScheme = SecuritySchemes.ApiBearer;
                authentication.DefaultChallengeScheme = SecuritySchemes.ApiBearer;
                authentication.DefaultForbidScheme = SecuritySchemes.ApiBearer;
            })
            .AddJwtBearer(
                SecuritySchemes.ApiBearer,
                jwt => ConfigureBearer(jwt, options, options.Audience))
            .AddCookie(SecuritySchemes.WebCookie, cookie =>
            {
                cookie.Cookie.Name = options.RequireSecureCookies
                    ? "__Host-campus-part05-web"
                    : "campus-part05-web";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.IsEssential = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = options.RequireSecureCookies
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                cookie.SlidingExpiration = true;
                cookie.Events.OnValidatePrincipal = ValidateSessionAsync;
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                cookie.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(SecuritySchemes.WebOidc, oidc =>
            {
                oidc.SignInScheme = SecuritySchemes.WebCookie;
                oidc.Authority = options.Authority;
                oidc.ClientId = options.WebClientId;
                oidc.ClientSecret = options.WebClientSecret;
                oidc.RequireHttpsMetadata = options.RequireHttpsMetadata;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.ResponseMode = options.RequireSecureCookies
                    ? OpenIdConnectResponseMode.FormPost
                    : OpenIdConnectResponseMode.Query;
                oidc.UsePkce = true;
                oidc.SaveTokens = true;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.MapInboundClaims = false;
                oidc.CallbackPath = "/signin-oidc";
                oidc.SignedOutCallbackPath = "/signout-callback-oidc";
                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.Scope.Add("campus.read");
                oidc.Scope.Add("campus.write");
                oidc.TokenValidationParameters.NameClaimType = "preferred_username";
                oidc.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
                oidc.CorrelationCookie.SameSite = options.RequireSecureCookies
                    ? SameSiteMode.None
                    : SameSiteMode.Lax;
                oidc.NonceCookie.SameSite = options.RequireSecureCookies
                    ? SameSiteMode.None
                    : SameSiteMode.Lax;
                oidc.CorrelationCookie.SecurePolicy = options.RequireSecureCookies
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                oidc.NonceCookie.SecurePolicy = options.RequireSecureCookies
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                oidc.PushedAuthorizationBehavior = PushedAuthorizationBehavior.UseIfAvailable;
            })
            .AddJwtBearer(
                SecuritySchemes.BackchannelLogout,
                jwt =>
                {
                    ConfigureBearer(jwt, options, options.WebClientId);
                    jwt.Events.OnMessageReceived = async context =>
                    {
                        if (context.Request.HasFormContentType)
                        {
                            var form = await context.Request.ReadFormAsync(context.HttpContext.RequestAborted);
                            context.Token = form["logout_token"].FirstOrDefault();
                        }
                    };
                });

        services.AddTransient<IClaimsTransformation, KeycloakRoleClaimsTransformation>();
        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, SameOwnerAuthorizationHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy("CampusRead", policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new ScopeRequirement("campus.read")))
            .AddPolicy("CampusWrite", policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new ScopeRequirement("campus.write")))
            .AddPolicy("AdminOnly", policy =>
                policy.RequireAuthenticatedUser().RequireRole("Admin"))
            .AddPolicy("WebUser", policy =>
            {
                policy.AddAuthenticationSchemes(SecuritySchemes.WebCookie);
                policy.RequireAuthenticatedUser();
            });

        services.AddCors(cors =>
        {
            cors.AddPolicy("CampusSpa", policy =>
            {
                policy.WithOrigins(options.AllowedOrigins)
                    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                    .WithHeaders("Authorization", "Content-Type", "If-Match")
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });

        services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiter.AddPolicy("api", context =>
            {
                var subject = context.User.FindFirstValue("sub");
                var partition = !string.IsNullOrWhiteSpace(subject)
                    ? $"user:{subject}"
                    : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
                return RateLimitPartition.GetTokenBucketLimiter(
                    partition,
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.ApiTokenLimit,
                        TokensPerPeriod = options.ApiTokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(options.ApiReplenishmentSeconds),
                        AutoReplenishment = true,
                        QueueLimit = 0,
                    });
            });
            rateLimiter.AddSlidingWindowLimiter("login", limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.SegmentsPerWindow = 6;
                limiter.QueueLimit = 0;
            });
            rateLimiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(CultureInfo.InvariantCulture);
                }

                await SecurityProblemDetails.WriteAsync(
                    context.HttpContext.Response,
                    StatusCodes.Status429TooManyRequests,
                    "Too many requests.",
                    "rate_limit_exceeded",
                    cancellationToken);
            };
        });

        return services;
    }

    private static void ValidateOptions(W6SecurityOptions options)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) ||
            authority.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(options.Audience) ||
            string.IsNullOrWhiteSpace(options.WebClientId) ||
            string.IsNullOrWhiteSpace(options.WebClientSecret))
        {
            throw new InvalidOperationException(
                "Security requires an absolute Authority, Audience, WebClientId, " +
                "and a WebClientSecret supplied outside appsettings.json.");
        }

        if (options.ClockSkewSeconds is < 0 or > 300 ||
            options.ApiTokenLimit <= 0 ||
            options.ApiTokensPerPeriod <= 0 ||
            options.ApiReplenishmentSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Security clock-skew and rate-limit settings must be positive and bounded.");
        }

        if (options.AllowedOrigins is not { Length: > 0 } ||
            options.AllowedOrigins.Any(origin =>
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") ||
                uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment)))
        {
            throw new InvalidOperationException(
                "Security:AllowedOrigins must contain exact HTTP(S) origins without paths.");
        }
    }

    private static void ConfigureBearer(
        JwtBearerOptions jwt,
        W6SecurityOptions options,
        string audience)
    {
        jwt.Authority = options.Authority;
        jwt.Audience = audience;
        jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
        jwt.MapInboundClaims = false;
        jwt.RefreshOnIssuerKeyNotFound = true;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Authority.TrimEnd('/'),
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
            NameClaimType = "preferred_username",
            RoleClaimType = ClaimTypes.Role,
        };
        jwt.Events ??= new JwtBearerEvents();
        jwt.Events.OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await SecurityProblemDetails.WriteAsync(
                context.Response,
                StatusCodes.Status401Unauthorized,
                "Authentication is required or the access token is invalid.",
                "authentication_required",
                context.HttpContext.RequestAborted);
        };
        jwt.Events.OnForbidden = context => SecurityProblemDetails.WriteAsync(
            context.Response,
            StatusCodes.Status403Forbidden,
            "The authenticated principal is not allowed to perform this operation.",
            "forbidden",
            context.HttpContext.RequestAborted);
    }

    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var revocations = context.HttpContext.RequestServices
            .GetRequiredService<SessionRevocationStore>();
        var sid = context.Principal?.FindFirstValue("sid");
        var sub = context.Principal?.FindFirstValue("sub") ??
                  context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (await revocations.IsRevokedAsync(
                sid,
                sub,
                context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(SecuritySchemes.WebCookie);
        }
    }
}
