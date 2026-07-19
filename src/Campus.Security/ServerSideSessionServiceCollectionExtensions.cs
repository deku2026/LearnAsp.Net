using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Campus.Security;

public static class ServerSideSessionServiceCollectionExtensions
{
    public static IServiceCollection AddCampusServerSideSessions(
        this IServiceCollection services,
        IConfiguration configuration,
        ServerSideSessionOptions options)
    {
        services.AddSingleton(Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);

        if (options.UseRedis)
        {
            var connectionString = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Redis is required when server-side sessions use Redis.");
            var multiplexer = ConnectionMultiplexer.Connect(connectionString);
            services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            services.AddStackExchangeRedisCache(redis =>
            {
                redis.Configuration = connectionString;
                redis.InstanceName = $"{options.ApplicationName}:";
            });
            services.AddDataProtection()
                .PersistKeysToStackExchangeRedis(
                    multiplexer,
                    $"{options.KeyPrefix}data-protection-keys")
                .SetApplicationName(options.ApplicationName);
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddDataProtection().SetApplicationName(options.ApplicationName);
        }

        services.AddSingleton<ServerSideTicketStore>();
        services.AddSingleton<SessionRevocationStore>();
        services.AddSingleton<
            IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>,
            ServerSideTicketStorePostConfigure>();
        return services;
    }
}
