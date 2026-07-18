using System.Net;
using System.Net.Http.Json;
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
    }

    [Fact]
    public async Task Endpoint_filter_blocks_code()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/courses", new CreateCourseRequest("BLOCKED", "Nope", 1));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Controller_courses_work()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/controller/v1/courses",
            new CreateCourseRequest("PHY101", "Physics", 3));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
