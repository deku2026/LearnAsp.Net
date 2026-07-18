using System.Net.Http.Json;
using System.Text.Json;

namespace Campus.Testing;

public static class ProblemDetailsAssertions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedErrorCode)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var doc = document.RootElement;

        if (doc.TryGetProperty("errorCode", out var direct))
        {
            if (!string.Equals(direct.GetString(), expectedErrorCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected errorCode '{expectedErrorCode}', got '{direct.GetString()}'.");
            }

            return;
        }

        if (doc.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("errorCode", out var nested))
        {
            if (!string.Equals(nested.GetString(), expectedErrorCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected errorCode '{expectedErrorCode}', got '{nested.GetString()}'.");
            }

            return;
        }

        throw new InvalidOperationException("Response JSON missing errorCode.");
    }
}
