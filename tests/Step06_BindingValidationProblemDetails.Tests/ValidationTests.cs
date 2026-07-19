using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Step06_BindingValidationProblemDetails.Tests;

public sealed class ValidationTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ValidationTests()
    {
        // Production environment so UseExceptionHandler handles instead of developer page.
        _factory = new CampusWebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Production"));
        _client = _factory.CreateClient();
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
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Code", out _));
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
            term = "2026S2",
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
    public async Task Async_fluent_validation_rejects_duplicate_section()
    {
        var course = await (await _client.PostAsJsonAsync(
                "/api/v1/courses",
                new { code = "ASYNC1", title = "Async validation", credits = 3 }))
            .Content.ReadFromJsonAsync<CourseDto>();
        var body = new { courseId = course!.Id, term = "2026F", capacity = 20 };
        (await _client.PostAsJsonAsync("/api/v1/sections", body)).EnsureSuccessStatusCode();

        var duplicate = await _client.PostAsJsonAsync("/api/v1/sections", body);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("section.duplicate", problem.GetProperty("errorCode").GetString());
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
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(
            !problem.TryGetProperty("detail", out var detail) ||
            detail.ValueKind is JsonValueKind.Null);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
