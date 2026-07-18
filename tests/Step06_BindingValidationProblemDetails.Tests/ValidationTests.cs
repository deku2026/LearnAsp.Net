using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;
using Microsoft.AspNetCore.Hosting;

namespace Step06_BindingValidationProblemDetails.Tests;

public sealed class ValidationTests
{
    private readonly HttpClient _client;

    public ValidationTests()
    {
        // Production environment so UseExceptionHandler handles instead of developer page.
        var factory = new CampusWebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Production"));
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Invalid_course_returns_bad_request()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "",
            title = "x",
            credits = 0,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Custom_validation_attribute_rejects_bad_term()
    {
        var courseResponse = await _client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "CS201",
            title = "Systems",
            credits = 3,
        });
        courseResponse.EnsureSuccessStatusCode();
        var course = await courseResponse.Content.ReadFromJsonAsync<CourseDto>();

        var response = await _client.PostAsJsonAsync("/api/v1/sections", new
        {
            courseId = course!.Id,
            term = "summer-2026",
            capacity = 30,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IValidatableObject_cross_field_rejects_intensive_odd()
    {
        var courseResponse = await _client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "CS201",
            title = "Systems",
            credits = 3,
        });
        courseResponse.EnsureSuccessStatusCode();
        var course = await courseResponse.Content.ReadFromJsonAsync<CourseDto>();

        var response = await _client.PostAsJsonAsync("/api/v1/sections", new
        {
            courseId = course!.Id,
            term = "INTENSIVE2026S1",
            capacity = 3,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Valid_course_created()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "CS301",
            title = "Compilers",
            credits = 4,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Exception_handler_maps_not_found_to_404()
    {
        var response = await _client.GetAsync("/api/v1/throw/notfound");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(
            json.TryGetProperty("errorCode", out var code) && code.GetString() == ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Exception_handler_maps_bad_arg_to_400()
    {
        var response = await _client.GetAsync("/api/v1/throw/badarg");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Exception_handler_maps_unknown_to_500()
    {
        var response = await _client.GetAsync("/api/v1/throw/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
