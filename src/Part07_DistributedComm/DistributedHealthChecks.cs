using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Part07_DistributedComm;

public sealed class CatalogDatabaseHealthCheck(CatalogStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await store.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Catalog PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("Catalog PostgreSQL rejected the probe.");
        }
        catch (NpgsqlException ex)
        {
            return HealthCheckResult.Unhealthy("Catalog PostgreSQL failed.", ex);
        }
    }
}

public sealed class EnrollmentDatabaseHealthCheck(EnrollmentStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await store.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Enrollment PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("Enrollment PostgreSQL rejected the probe.");
        }
        catch (NpgsqlException ex)
        {
            return HealthCheckResult.Unhealthy("Enrollment PostgreSQL failed.", ex);
        }
    }
}

public sealed class GatewayDependenciesHealthCheck(
    IHttpClientFactory clients,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        foreach (var key in new[]
                 {
                     "Distributed:CatalogHttpUrl",
                     "Distributed:EnrollmentUrl",
                     "Distributed:NoticesUrl",
                 })
        {
            var baseUrl = configuration[key];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return HealthCheckResult.Unhealthy($"{key} is not configured.");
            }

            try
            {
                using var response = await clients.CreateClient("gateway-health")
                    .GetAsync(new Uri(new Uri(baseUrl), "health/ready"), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return HealthCheckResult.Unhealthy(
                        $"{key} returned {(int)response.StatusCode}.");
                }
            }
            catch (HttpRequestException ex)
            {
                return HealthCheckResult.Unhealthy($"{key} is unreachable.", ex);
            }
        }

        return HealthCheckResult.Healthy("All gateway destinations are ready.");
    }
}

public sealed class GatewayHeaderFilter(IConfiguration configuration) : IEndpointFilter
{
    private readonly byte[] _expectedToken = Encoding.UTF8.GetBytes(
        configuration["GatewayAuth:InternalToken"]
        ?? throw new InvalidOperationException(
            "GatewayAuth:InternalToken is required by internal services."));

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var user = context.HttpContext.Request.Headers["X-Campus-User"].ToString();
        var presentedToken = Encoding.UTF8.GetBytes(
            context.HttpContext.Request.Headers["X-Campus-Gateway"].ToString());
        var trustedGateway = presentedToken.Length == _expectedToken.Length &&
            CryptographicOperations.FixedTimeEquals(presentedToken, _expectedToken);
        if (string.IsNullOrWhiteSpace(user) || !trustedGateway)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Trusted gateway identity required");
        }

        return await next(context);
    }
}
