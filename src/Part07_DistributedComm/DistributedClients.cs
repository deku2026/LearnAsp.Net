using Grpc.Core;
using Part07_DistributedComm.Grpc;

namespace Part07_DistributedComm;

public sealed class CatalogGrpcClient(Catalog.CatalogClient client)
{
    public async Task<CatalogCourse?> GetAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        try
        {
            var reply = await client.GetCourseAsync(
                new GetCourseRequest { CourseId = courseId.ToString() },
                cancellationToken: cancellationToken);
            return new CatalogCourse(
                Guid.Parse(reply.CourseId),
                reply.Code,
                reply.Title,
                reply.AvailableSeats);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }
}

public sealed class NoticesClient(HttpClient httpClient)
{
    public async Task<NoticesProbeResult> ProbeAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            "internal/fault/probe",
            cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<NoticesProbeResult>(
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return result ?? throw new InvalidOperationException(
            "Notices probe returned no body.");
    }
}

public sealed record NoticesProbeResult(int RequestCount, string Status);

public sealed class FaultInjectionState
{
    private int _remainingFailures;
    private int _requestCount;

    public int RequestCount => Volatile.Read(ref _requestCount);

    public void Configure(int failures)
    {
        Interlocked.Exchange(ref _remainingFailures, Math.Max(0, failures));
        Interlocked.Exchange(ref _requestCount, 0);
    }

    public bool ShouldFail()
    {
        Interlocked.Increment(ref _requestCount);
        while (true)
        {
            var remaining = Volatile.Read(ref _remainingFailures);
            if (remaining <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref _remainingFailures,
                    remaining - 1,
                    remaining) == remaining)
            {
                return true;
            }
        }
    }
}
