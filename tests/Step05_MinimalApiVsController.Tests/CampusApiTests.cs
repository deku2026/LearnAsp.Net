using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Contracts;
using Campus.Testing;

namespace Step05_MinimalApiVsController.Tests;

public sealed class CampusApiTests : IClassFixture<CampusWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CampusApiTests(CampusWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Enrollment_flow_works()
    {
        var course = await (await _client.PostAsJsonAsync("/api/v1/courses", new CreateCourseRequest("MATH101", "Calculus", 4)))
            .Content.ReadFromJsonAsync<CourseDto>();
        Assert.NotNull(course);

        var section = await (await _client.PostAsJsonAsync("/api/v1/sections", new CreateSectionRequest(course.Id, "2026S1", 1)))
            .Content.ReadFromJsonAsync<SectionDto>();
        Assert.NotNull(section);

        var studentA = Guid.NewGuid();
        var studentB = Guid.NewGuid();

        var e1 = await (await _client.PostAsJsonAsync("/api/v1/enrollments", new CreateEnrollmentRequest(studentA, section.Id)))
            .Content.ReadFromJsonAsync<EnrollmentDto>();
        Assert.Equal(EnrollmentStatus.Confirmed, e1!.Status);

        var e2 = await (await _client.PostAsJsonAsync("/api/v1/enrollments", new CreateEnrollmentRequest(studentB, section.Id)))
            .Content.ReadFromJsonAsync<EnrollmentDto>();
        Assert.Equal(EnrollmentStatus.Waitlisted, e2!.Status);

        var cancelled = await (await _client.PostAsync($"/api/v1/enrollments/{e1.Id}/cancel", null))
            .Content.ReadFromJsonAsync<EnrollmentDto>();
        Assert.Equal(EnrollmentStatus.Cancelled, cancelled!.Status);

        var enrollments = await _client.GetFromJsonAsync<List<EnrollmentDto>>("/api/v1/enrollments");
        Assert.Contains(enrollments!, enrollment => enrollment.Id == e2.Id && enrollment.Status == EnrollmentStatus.Confirmed);
    }

    [Fact]
    public async Task Endpoint_filter_blocks_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/courses", new CreateCourseRequest("BLOCKED", "Nope", 1));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_filters_execute_fifo_before_and_filo_after()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/courses",
            new CreateCourseRequest("FILTER101", "Filter order", 2));
        response.EnsureSuccessStatusCode();
        Assert.Equal(
            "first-in,second-in,handler,second-out,first-out",
            response.Headers.GetValues("X-Filter-Order").Single());
    }

    [Fact]
    public async Task Minimal_course_crud_supports_update_and_delete()
    {
        var created = await (await _client.PostAsJsonAsync(
                "/api/v1/courses",
                new CreateCourseRequest("CRUD101", "Before", 2)))
            .Content.ReadFromJsonAsync<CourseDto>();

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/v1/courses/{created!.Id}",
            new CreateCourseRequest("CRUD102", "After", 3));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<CourseDto>();
        Assert.Equal("After", updated!.Title);

        var deleteResponse = await _client.DeleteAsync($"/api/v1/courses/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/courses/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Explicit_binding_sources_are_honored()
    {
        var id = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/binding/{id}?q=campus");
        request.Headers.Add("X-College-Id", "engineering");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, payload.GetProperty("id").GetGuid());
        Assert.Equal("campus", payload.GetProperty("q").GetString());
        Assert.Equal("engineering", payload.GetProperty("collegeId").GetString());
    }

    [Fact]
    public async Task Controller_courses_work()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/controller/v1/courses",
            new CreateCourseRequest("PHY101", "Physics", 3));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Controller-Resource-Filter"));
        Assert.True(response.Headers.Contains("X-Controller-Elapsed-ms"));
    }

    [Fact]
    public async Task ApiController_automatically_returns_validation_problem()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/controller/v1/courses",
            new { code = "", title = "x", credits = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Controller_exception_filter_returns_problem_details()
    {
        var response = await _client.GetAsync("/api/controller/v1/courses/throw");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ErrorCodes.InternalError, payload.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Concurrent_enrollment_never_overbooks_a_section()
    {
        var course = await (await _client.PostAsJsonAsync(
                "/api/v1/courses",
                new CreateCourseRequest($"CON{Guid.NewGuid():N}"[..12], "Concurrency", 3)))
            .Content.ReadFromJsonAsync<CourseDto>();
        var section = await (await _client.PostAsJsonAsync(
                "/api/v1/sections",
                new CreateSectionRequest(course!.Id, "2026F", 1)))
            .Content.ReadFromJsonAsync<SectionDto>();

        var requests = Enumerable.Range(0, 20)
            .Select(_ => _client.PostAsJsonAsync(
                "/api/v1/enrollments",
                new CreateEnrollmentRequest(Guid.NewGuid(), section!.Id)));
        var responses = await Task.WhenAll(requests);
        var enrollments = await Task.WhenAll(
            responses.Select(response => response.Content.ReadFromJsonAsync<EnrollmentDto>()));

        Assert.Single(enrollments, enrollment => enrollment!.Status == EnrollmentStatus.Confirmed);
        Assert.Equal(19, enrollments.Count(enrollment => enrollment!.Status == EnrollmentStatus.Waitlisted));
    }

    [Fact]
    public async Task Sse_stream_has_event_stream_content_type()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/enrollments/stream");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }
}
