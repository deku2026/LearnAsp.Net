using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Campus.Testing;

public class CampusWebApplicationFactory<TEntry> : WebApplicationFactory<TEntry>
    where TEntry : class
{
    private readonly Dictionary<string, string?> _config = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jwt:Issuer"] = "campus-tests",
        ["Jwt:Audience"] = "campus-tests",
        ["Jwt:SigningKey"] = "campus-tests-signing-key-at-least-32-bytes",
    };

    public CampusWebApplicationFactory<TEntry> WithSetting(string key, string? value)
    {
        _config[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(_config);
        });
    }
}

public sealed class TestAuthWebApplicationFactory<TEntry> : CampusWebApplicationFactory<TEntry>
    where TEntry : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ => { });
        });
    }
}
