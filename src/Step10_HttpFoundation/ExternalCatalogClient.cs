using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Step10_HttpFoundation;

public interface IExternalCatalogClient
{
    Task<ExternalCourse?> GetByCodeAsync(string code, CancellationToken ct = default);
}

public sealed class ExternalCatalogClient(HttpClient http) : IExternalCatalogClient
{
    public async Task<ExternalCourse?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"/catalog/{Uri.EscapeDataString(code)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExternalCourse>(cancellationToken: ct);
    }
}

public sealed record ExternalCourse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("provider")] string Provider);
