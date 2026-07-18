using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Testing;

namespace Part03_3_ProjectStructure.Tests;

public sealed class StructureTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public StructureTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Composition_root_creates_course_via_handler()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/courses", new { code = "S301", title = "Structure", credits = 3 });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("S301", body.GetProperty("code").GetString());

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/v1/courses");
        Assert.True(list.GetArrayLength() >= 1);
    }
}
