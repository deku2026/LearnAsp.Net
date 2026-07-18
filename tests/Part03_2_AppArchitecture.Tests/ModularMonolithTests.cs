using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Testing;

namespace Part03_2_AppArchitecture.Tests;

public sealed class ModularMonolithTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ModularMonolithTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Enroll_confirmed_creates_notice_via_outbox()
    {
        var course = await (await _client.PostAsJsonAsync("/api/v1/courses", new { code = "M1", title = "Mod", credits = 3 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var section = await (await _client.PostAsJsonAsync("/api/v1/sections", new
        {
            courseId = course.GetProperty("id").GetGuid(),
            term = "2026F",
            capacity = 2,
        })).Content.ReadFromJsonAsync<JsonElement>();

        var enroll = await _client.PostAsJsonAsync("/api/v1/enrollments", new
        {
            studentId = Guid.NewGuid(),
            sectionId = section.GetProperty("id").GetGuid(),
        });
        Assert.Equal(HttpStatusCode.Created, enroll.StatusCode);
        var body = await enroll.Content.ReadFromJsonAsync<JsonElement>();
        var status = body.GetProperty("status");
        var statusText = status.ValueKind == JsonValueKind.String
            ? status.GetString()
            : status.GetInt32().ToString();
        Assert.True(
            statusText is "Confirmed" or "1",
            $"unexpected status: {status}");

        // Outbox processor polls every 50ms
        JsonElement notices = default;
        for (var i = 0; i < 40; i++)
        {
            notices = await _client.GetFromJsonAsync<JsonElement>("/api/v1/notices");
            if (notices.GetArrayLength() > 0)
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.True(notices.GetArrayLength() >= 1);
    }
}
