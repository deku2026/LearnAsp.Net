using System.Security.Claims;
using Campus.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Part05_1_AuthnAuthz.Tests;

public static class AuthnAuthzFactory
{
    public static TestAuthWebApplicationFactory<Program> Create()
    {
        var factory = new TestAuthWebApplicationFactory<Program>();
        factory.WithSetting("Security:UseRedis", "false");
        factory.WithSetting("Security:Authority", "https://issuer.invalid/realms/test");
        factory.WithSetting("Security:WebClientSecret", "test-only");
        factory.WithSetting("Security:ApiTokenLimit", "100");
        return factory;
    }
}

public sealed class RealJwtFactory : CampusWebApplicationFactory<Program>, IDisposable
{
    private readonly System.Security.Cryptography.RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public RealJwtFactory()
    {
        _rsa = System.Security.Cryptography.RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa)
        {
            KeyId = "test-signing-key",
        };
        WithSetting("Security:UseRedis", "false");
        WithSetting("Security:Authority", Issuer);
        WithSetting("Security:Audience", Audience);
        WithSetting("Security:WebClientSecret", "test-only");
        WithSetting("Security:ClockSkewSeconds", "0");
        WithSetting("Security:ApiTokenLimit", "100");
    }

    public const string Issuer = "https://issuer.example/realms/campus";
    public const string Audience = "campus-api";

    public string IssueToken(
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", "jwt-user"),
                new Claim("preferred_username", "jwt-user"),
                new Claim("scope", "campus.read campus.write"),
            ]),
            IssuedAt = DateTime.UtcNow.AddSeconds(-1),
            NotBefore = DateTime.UtcNow.AddSeconds(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                signingKey ?? _signingKey,
                SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(SecuritySchemes.ApiBearer, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(_signingKey);
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = Audience;
                options.TokenValidationParameters.IssuerSigningKey = _signingKey;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rsa.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed class AuthnAuthzFixture : IDisposable
{
    public TestAuthWebApplicationFactory<Program> TestAuth { get; } =
        AuthnAuthzFactory.Create();

    public RealJwtFactory RealJwt { get; } = new();

    public void Dispose()
    {
        TestAuth.Dispose();
        RealJwt.Dispose();
    }
}
