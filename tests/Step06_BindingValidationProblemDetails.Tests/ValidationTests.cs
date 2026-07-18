using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;

namespace Step06_BindingValidationProblemDetails.Tests;

public sealed class ValidationTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ValidationTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Invalid_course_returns_bad_request()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/courses", new CreateCourseBody
        {
            Code = "",
            Title = "x",
            Credits = 0,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Intensive_odd_capacity_fails_fluent_rule()
    {
        var courseResponse = await _client.PostAsJsonAsync("/api/v1/courses", new CreateCourseBody
        {
            Code = "CS201",
            Title = "Systems",
            Credits = 3,
        });
        courseResponse.EnsureSuccessStatusCode();
        var course = await courseResponse.Content.ReadFromJsonAsync<CourseDto>();

        var response = await _client.PostAsJsonAsync("/api/v1/sections", new CreateSectionBody
        {
            CourseId = course!.Id,
            Term = "INTENSIVE-A",
            Capacity = 3,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(
            json.TryGetProperty("errorCode", out _) ||
            json.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Valid_course_created()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/courses", new CreateCourseBody
        {
            Code = "CS301",
            Title = "Compilers",
            Credits = 4,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
