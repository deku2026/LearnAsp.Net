using System.Net;
using System.Net.Http.Json;
using Campus.Contracts;
using Campus.Testing;

namespace Step09_IntegrationTesting.Tests;

[Collection(PostgresCollection.Name)]
public sealed class IntegrationTests
{
    private readonly PostgresFixture _fx;

    public IntegrationTests(PostgresFixture fx) => _fx = fx;

    private void EnsurePg()
    {
        Assert.True(
            _fx.IsAvailable,
            _fx.SkipReason ?? "PostgreSQL unavailable (start docker compose postgres or enable Docker for Testcontainers).");
    }

    [Fact]
    public async Task Admin_course_section_student_enrollment_flow()
    {
        EnsurePg();
        await _fx.ResetAsync();
        await using var factory = _fx.CreateFactory();
        var admin = factory.CreateClient().AsTestUser("admin-1", "Admin");
        var student = factory.CreateClient().AsTestUser("stu-1", "Student");

        var courseResp = await admin.PostAsJsonAsync("/api/v1/courses", new { code = "CS501", title = "Distributed Systems", credits = 3 });
        Assert.Equal(HttpStatusCode.Created, courseResp.StatusCode);
        var course = await courseResp.Content.ReadFromJsonAsync<CourseDto>();
        Assert.NotNull(course);

        var sectionResp = await admin.PostAsJsonAsync("/api/v1/sections", new { courseId = course.Id, term = "2026F", capacity = 1 });
        Assert.Equal(HttpStatusCode.Created, sectionResp.StatusCode);
        var section = await sectionResp.Content.ReadFromJsonAsync<SectionDto>();
        Assert.NotNull(section);

        var enrollResp = await student.PostAsJsonAsync("/api/v1/enrollments", new CreateEnrollmentRequest(Guid.Empty, section.Id));
        Assert.Equal(HttpStatusCode.Created, enrollResp.StatusCode);
        var enrollment = await enrollResp.Content.ReadFromJsonAsync<EnrollmentDto>();
        Assert.Equal(EnrollmentStatus.Confirmed, enrollment!.Status);

        var list = await student.GetFromJsonAsync<List<EnrollmentDto>>("/api/v1/enrollments");
        Assert.NotNull(list);
        Assert.Contains(list, e => e.Id == enrollment.Id);
    }

    [Fact]
    public async Task Validation_failure_is_400()
    {
        EnsurePg();
        await _fx.ResetAsync();
        await using var factory = _fx.CreateFactory();
        var admin = factory.CreateClient().AsTestUser("admin-1", "Admin");

        var response = await admin.PostAsJsonAsync("/api/v1/courses", new { code = "", title = "x", credits = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_create_course_is_401()
    {
        EnsurePg();
        await _fx.ResetAsync();
        await using var factory = _fx.CreateFactory();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/courses", new { code = "X1", title = "Nope", credits = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Student_create_course_is_403()
    {
        EnsurePg();
        await _fx.ResetAsync();
        await using var factory = _fx.CreateFactory();
        var student = factory.CreateClient().AsTestUser("stu-2", "Student");
        var response = await student.PostAsJsonAsync("/api/v1/courses", new { code = "X2", title = "Nope", credits = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Respawn_isolates_tests_empty_list()
    {
        EnsurePg();
        await _fx.ResetAsync();
        await using var factory = _fx.CreateFactory();
        var student = factory.CreateClient().AsTestUser("stu-3", "Student");
        var list = await student.GetFromJsonAsync<List<EnrollmentDto>>("/api/v1/enrollments");
        Assert.NotNull(list);
        Assert.Empty(list);
    }
}
