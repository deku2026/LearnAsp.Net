using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Testing;

namespace Part03_1_ApiDesign.Tests;

public sealed class ApiDesignTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiDesignTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_course_and_keyset_list()
    {
        for (var i = 0; i < 3; i++)
        {
            var r = await _client.PostAsJsonAsync("/api/v1/courses", new { code = $"C{i:00}", title = $"Course {i}", credits = 3 });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        }

        var page = await _client.GetFromJsonAsync<JsonElement>("/api/v1/courses?limit=2");
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(page.GetProperty("nextCursor").GetString()));
    }

    [Fact]
    public async Task Etag_if_match_and_if_none_match()
    {
        var created = await _client.PostAsJsonAsync("/api/v1/courses", new { code = "ETAG1", title = "ETag", credits = 2 });
        var course = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = course.GetProperty("id").GetGuid();

        var get1 = await _client.GetAsync($"/api/v1/courses/{id}");
        var etag = get1.Headers.ETag!.Tag;

        using (var get304 = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/courses/{id}"))
        {
            get304.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
            var r304 = await _client.SendAsync(get304);
            Assert.Equal(HttpStatusCode.NotModified, r304.StatusCode);
        }

        var put428 = await _client.PutAsJsonAsync($"/api/v1/courses/{id}", new { title = "NoEtag", credits = 2 });
        Assert.Equal((HttpStatusCode)428, put428.StatusCode);

        using (var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/courses/{id}")
        {
            Content = JsonContent.Create(new { title = "Updated", credits = 4 }),
        })
        {
            put.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
            var putOk = await _client.SendAsync(put);
            Assert.Equal(HttpStatusCode.OK, putOk.StatusCode);
        }
    }

    [Fact]
    public async Task Idempotency_key_replays_same_enrollment()
    {
        var course = await (await _client.PostAsJsonAsync("/api/v1/courses", new { code = "IDEM", title = "Idem", credits = 1 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var section = await (await _client.PostAsJsonAsync("/api/v1/sections", new
        {
            courseId = course.GetProperty("id").GetGuid(),
            term = "2026F",
            capacity = 10,
        })).Content.ReadFromJsonAsync<JsonElement>();

        var body = new { studentId = Guid.NewGuid(), sectionId = section.GetProperty("id").GetGuid() };

        JsonElement e1;
        using (var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/enrollments") { Content = JsonContent.Create(body) })
        {
            req1.Headers.Add("Idempotency-Key", "key-1");
            var r1 = await _client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
            e1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        }

        using (var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/enrollments") { Content = JsonContent.Create(body) })
        {
            req2.Headers.Add("Idempotency-Key", "key-1");
            var r2 = await _client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
            var e2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(e1.GetProperty("id").GetGuid(), e2.GetProperty("id").GetGuid());
        }
    }

    [Fact]
    public async Task V2_enrollment_shape_differs()
    {
        var course = await (await _client.PostAsJsonAsync("/api/v1/courses", new { code = "V2C", title = "V2", credits = 1 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var section = await (await _client.PostAsJsonAsync("/api/v1/sections", new
        {
            courseId = course.GetProperty("id").GetGuid(),
            term = "2026F",
            capacity = 5,
        })).Content.ReadFromJsonAsync<JsonElement>();

        var r = await _client.PostAsJsonAsync("/api/v2/enrollments", new
        {
            studentId = Guid.NewGuid(),
            sectionId = section.GetProperty("id").GetGuid(),
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("enrollmentId", out _));
        Assert.True(json.TryGetProperty("state", out _));
    }

    [Fact]
    public async Task Deprecated_endpoint_sets_headers()
    {
        var r = await _client.GetAsync("/api/v1/legacy/ping");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(r.Headers.Contains("Deprecation"));
        Assert.True(r.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task Openapi_documents_exist_with_core_paths()
    {
        var v1 = await _client.GetAsync("/openapi/v1.json");
        var v2 = await _client.GetAsync("/openapi/v2.json");
        Assert.Equal(HttpStatusCode.OK, v1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, v2.StatusCode);
        var body = await v1.Content.ReadAsStringAsync();
        Assert.Contains("courses", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enrollments", body, StringComparison.OrdinalIgnoreCase);
    }
}
