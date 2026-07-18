using System.Net;
using System.Net.Http.Json;
using Campus.Contracts;
using Campus.Testing;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Step09_IntegrationTesting.Tests;

[Collection(PostgresCollection.Name)]
public sealed class IntegrationTests
{
    private readonly PostgresFixture _fx;

    public IntegrationTests(PostgresFixture fx) => _fx = fx;

    private void EnsurePg()
    {
        Skip.If(
            !_fx.IsAvailable,
            _fx.SkipReason ?? "PostgreSQL unavailable (Docker/Testcontainers or localhost:5432).");
    }

    private async Task WithFactoryAsync(Func<WebApplicationFactory<Program>, Task> test)
    {
        EnsurePg();
        await _fx.ResetAsync();
        await _fx.UsingFactoryAsync(test);
    }

    [SkippableFact]
    public Task Admin_course_section_student_enrollment_flow()
        => WithFactoryAsync(async factory =>
        {
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
        });

    [SkippableFact]
    public Task Validation_failure_is_400()
        => WithFactoryAsync(async factory =>
        {
            var admin = factory.CreateClient().AsTestUser("admin-1", "Admin");
            var response = await admin.PostAsJsonAsync("/api/v1/courses", new { code = "", title = "x", credits = 0 });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        });

    [SkippableFact]
    public Task Anonymous_create_course_is_401()
        => WithFactoryAsync(async factory =>
        {
            var client = factory.CreateClient();
            var response = await client.PostAsJsonAsync("/api/v1/courses", new { code = "X1", title = "Nope", credits = 1 });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });

    [SkippableFact]
    public Task Student_create_course_is_403()
        => WithFactoryAsync(async factory =>
        {
            var student = factory.CreateClient().AsTestUser("stu-2", "Student");
            var response = await student.PostAsJsonAsync("/api/v1/courses", new { code = "X2", title = "Nope", credits = 1 });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        });

    [SkippableFact]
    public Task Respawn_isolates_tests_empty_list()
        => WithFactoryAsync(async factory =>
        {
            var student = factory.CreateClient().AsTestUser("stu-3", "Student");
            var list = await student.GetFromJsonAsync<List<EnrollmentDto>>("/api/v1/enrollments");
            Assert.NotNull(list);
            Assert.Empty(list);
        });

    [SkippableFact]
    public async Task Migrations_history_table_exists_after_startup()
    {
        EnsurePg();
        await using var conn = new Npgsql.NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
        Assert.True(count >= 1, $"expected at least 1 migration row, got {count}");
    }
}
