using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;

namespace Step07_AuthnAuthzEntry.Tests;

public sealed class AuthTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly CampusWebApplicationFactory<Program> _factory;

    public AuthTests(CampusWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_me_is_unauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Student_cannot_create_course()
    {
        var client = _factory.CreateClient().AsTestUser(role: "Student");
        var response = await client.PostAsJsonAsync("/api/v1/courses", new CreateCourseRequest("X", "Y", 1));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_course()
    {
        var client = _factory.CreateClient().AsTestUser(userId: "admin-1", role: "Admin");
        var response = await client.PostAsJsonAsync("/api/v1/courses", new CreateCourseRequest("CS401", "AI", 3));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Student_can_enroll_and_me_works()
    {
        var client = _factory.CreateClient().AsTestUser(userId: "stu-9", role: "Student", collegeId: "eng");
        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("stu-9", me.GetProperty("sub").GetString());

        var enroll = await client.PostAsJsonAsync(
            "/api/v1/enrollments",
            new CreateEnrollmentRequest(Guid.Empty, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, enroll.StatusCode);
    }

    [Fact]
    public async Task Jwt_token_endpoint_works()
    {
        var client = _factory.CreateClient();
        var tokenResponse = await client.PostAsJsonAsync("/token/dev", new { sub = "jwt-user", role = "Admin", collegeId = "c1" });
        tokenResponse.EnsureSuccessStatusCode();
        var payload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
