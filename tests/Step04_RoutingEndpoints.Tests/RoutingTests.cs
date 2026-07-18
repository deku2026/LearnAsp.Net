using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Testing;

namespace Step04_RoutingEndpoints.Tests;

public sealed class RoutingTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RoutingTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_course_by_guid_works()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var response = await _client.GetAsync($"/api/v1/courses/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_guid_segment_is_not_matched()
    {
        var response = await _client.GetAsync("/api/v1/courses/not-a-guid");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Link_generator_returns_href()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var json = await _client.GetFromJsonAsync<JsonElement>($"/links/course/{id}");
        var href = json.GetProperty("href").GetString();
        Assert.False(string.IsNullOrWhiteSpace(href));
        Assert.Contains(id.ToString(), href, StringComparison.OrdinalIgnoreCase);
    }
}
