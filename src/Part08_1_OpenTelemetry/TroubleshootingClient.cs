namespace Part08_1_OpenTelemetry;

public sealed class TroubleshootingClient(HttpClient client)
{
    public async Task<DownstreamResult> GetWorkAsync(
        Guid workId,
        int delayMs,
        CancellationToken cancellationToken)
    {
        var result = await client.GetFromJsonAsync<DownstreamResult>(
            $"/api/downstream/{workId}?delayMs={delayMs}",
            cancellationToken);
        return result ?? throw new InvalidOperationException(
            "Troubleshooting service returned an empty response.");
    }
}

public sealed record DownstreamResult(Guid WorkId, int DelayMs, string Status);
