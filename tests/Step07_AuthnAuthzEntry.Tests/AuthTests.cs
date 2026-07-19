using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Step07_AuthnAuthzEntry.Tests;

public sealed class AuthTests : IClassFixture<TestAuthWebApplicationFactory<Program>>
{
    private readonly TestAuthWebApplicationFactory<Program> _factory;

    public AuthTests(TestAuthWebApplicationFactory<Program> factory)
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
    public async Task Fallback_policy_requires_auth_for_unprotected_endpoint()
    {
        // No RequireAuthorization call: the fallback policy still secures this endpoint.
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/default-protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowAnonymous_overrides_fallback_policy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/enrollments/public-count");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
    public async Task Student_cannot_enroll_another_student()
    {
        var client = _factory.CreateClient().AsTestUser(userId: "stu-owner", role: "Student");
        var response = await client.PostAsJsonAsync(
            "/api/v1/enrollments",
            new CreateEnrollmentRequest(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Jwt_token_endpoint_works()
    {
        await using var jwtFactory = new CampusWebApplicationFactory<Program>();
        var client = jwtFactory.CreateClient();
        var tokenResponse = await client.PostAsJsonAsync("/token/dev", new { sub = "jwt-user", role = "Admin", collegeId = "c1" });
        tokenResponse.EnsureSuccessStatusCode();
        var payload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Test_headers_cannot_authenticate_in_the_real_application()
    {
        await using var realFactory = new CampusWebApplicationFactory<Program>();
        var client = realFactory.CreateClient().AsTestUser(role: "Admin");
        var response = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Development_token_endpoint_is_not_exposed_in_production()
    {
        await using var productionFactory = new CampusWebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:SigningKey"] = "production-test-signing-key-at-least-32-bytes",
                    }));
            });
        var client = productionFactory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/token/dev",
            new { sub = "user", role = "Admin", collegeId = "c1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
