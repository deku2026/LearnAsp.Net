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

    [Theory]
    [InlineData("2026S1", HttpStatusCode.OK)]
    [InlineData("2026F2", HttpStatusCode.OK)]
    [InlineData("invalid", HttpStatusCode.NotFound)]
    public async Task Custom_constraint_matches_term_codes(string term, HttpStatusCode expected)
    {
        var response = await _client.GetAsync($"/api/v1/sections/{term}");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Nested_group_inherits_prefix_and_filter()
    {
        // /api/v1/admin/stats — admin group inherits /api/v1 prefix + group filter header.
        var response = await _client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Admin-Filter"));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("totalCourses").GetInt32() >= 1);
    }
}
