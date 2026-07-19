using System.Security.Claims;
using Campus.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Part05_2_SpaAuth;

public static class BffSecurityConfiguration
{
    public static IServiceCollection AddCampusBff(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection("Bff").Get<BffOptions>() ?? new BffOptions();
        ValidateOptions(options);
        services.AddSingleton(Options.Create(options));
        services.AddCampusServerSideSessions(
            configuration,
            new ServerSideSessionOptions
            {
                CookieScheme = BffSchemes.Cookie,
                ApplicationName = "Campus.Part05_2.Bff",
                KeyPrefix = "Campus:Part05_2:",
                UseRedis = options.UseRedis,
            });

        services
            .AddAuthentication(authentication =>
            {
                authentication.DefaultScheme = BffSchemes.Cookie;
                authentication.DefaultAuthenticateScheme = BffSchemes.Cookie;
                authentication.DefaultChallengeScheme = BffSchemes.Oidc;
                authentication.DefaultSignOutScheme = BffSchemes.Oidc;
            })
            .AddCookie(BffSchemes.Cookie, cookie =>
            {
                cookie.Cookie.Name = options.RequireSecureCookies
                    ? "__Host-campus-bff"
                    : "campus-bff";
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
            .AddOpenIdConnect(BffSchemes.Oidc, oidc =>
            {
                oidc.SignInScheme = BffSchemes.Cookie;
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
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
            .AddJwtBearer(BffSchemes.BackchannelLogout, jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.ClientId;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority.TrimEnd('/'),
                    ValidateAudience = true,
                    ValidAudience = options.ClientId,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = async context =>
                    {
                        if (context.Request.HasFormContentType)
                        {
                            var form = await context.Request.ReadFormAsync(context.HttpContext.RequestAborted);
                            context.Token = form["logout_token"].FirstOrDefault();
                        }
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("BffUser", policy =>
            {
                policy.AddAuthenticationSchemes(BffSchemes.Cookie);
                policy.RequireAuthenticatedUser();
            });

        services.AddHttpClient("oidc-backchannel");
        services.AddSingleton<BffAccessTokenService>();
        services.AddReverseProxy()
            .LoadFromMemory(
                [
                    new RouteConfig
                    {
                        RouteId = "campus-api",
                        ClusterId = "campus-api",
                        Match = new RouteMatch { Path = "/bff/api/{**catch-all}" },
                        AuthorizationPolicy = "BffUser",
                        Transforms =
                        [
                            new Dictionary<string, string>
                            {
                                ["PathRemovePrefix"] = "/bff",
                            },
                        ],
                    },
                ],
                [
                    new ClusterConfig
                    {
                        ClusterId = "campus-api",
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            ["primary"] = new() { Address = $"{options.DownstreamApi.TrimEnd('/')}/" },
                        },
                    },
                ])
            .AddTransforms(transform =>
            {
                if (transform.Route.RouteId != "campus-api")
                {
                    return;
                }

                transform.AddRequestTransform(context =>
                {
                    if (context.HttpContext.Items.TryGetValue(
                            BffAccessTokenService.HttpContextItemName,
                            out var value) &&
                        value is string accessToken)
                    {
                        context.ProxyRequest.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                    }

                    return ValueTask.CompletedTask;
                });
            });

        return services;
    }

    private static void ValidateOptions(BffOptions options)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) ||
            authority.Scheme is not ("http" or "https") ||
            !Uri.TryCreate(options.DownstreamApi, UriKind.Absolute, out var downstream) ||
            downstream.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "Bff requires absolute Authority and DownstreamApi values, ClientId, " +
                "and a ClientSecret supplied outside appsettings.json.");
        }

        if (options.RefreshBeforeExpirySeconds < 0)
        {
            throw new InvalidOperationException(
                "Bff:RefreshBeforeExpirySeconds cannot be negative.");
        }

        if (!string.IsNullOrWhiteSpace(options.PublicOrigin) &&
            (!Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var publicOrigin) ||
             publicOrigin.Scheme is not ("http" or "https") ||
             publicOrigin.AbsolutePath != "/" ||
             !string.IsNullOrEmpty(publicOrigin.Query) ||
             !string.IsNullOrEmpty(publicOrigin.Fragment)))
        {
            throw new InvalidOperationException(
                "Bff:PublicOrigin must be an exact HTTP(S) origin without a path.");
        }
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
            await context.HttpContext.SignOutAsync(BffSchemes.Cookie);
        }
    }
}
