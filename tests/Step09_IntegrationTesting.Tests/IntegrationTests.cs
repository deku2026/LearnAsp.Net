using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        Assert.SkipWhen(
            !_fx.IsAvailable,
            _fx.SkipReason ?? "PostgreSQL unavailable (Docker/Testcontainers or localhost:5432).");
    }

    private async Task WithFactoryAsync(Func<WebApplicationFactory<Program>, Task> test)
    {
        EnsurePg();
        await _fx.ResetAsync();
        await _fx.UsingFactoryAsync(test);
    }

    [Fact]
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

    [Fact]
    public Task Validation_failure_is_400()
        => WithFactoryAsync(async factory =>
        {
            var admin = factory.CreateClient().AsTestUser("admin-1", "Admin");
            var response = await admin.PostAsJsonAsync("/api/v1/courses", new { code = "", title = "x", credits = 0 });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await ProblemDetailsAssertions.AssertErrorCodeAsync(response, ErrorCodes.ValidationFailed);
        });

    [Fact]
    public Task Course_crud_uses_real_postgres()
        => WithFactoryAsync(async factory =>
        {
            var admin = factory.CreateClient().AsTestUser("admin-crud", "Admin");
            var createdResponse = await admin.PostAsJsonAsync(
                "/api/v1/courses",
                new { code = "CRUD1", title = "Before", credits = 2 });
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            Assert.NotNull(createdResponse.Headers.Location);
            var created = await createdResponse.Content.ReadFromJsonAsync<CourseDto>();
            Assert.NotNull(created);

            var read = await admin.GetFromJsonAsync<CourseDto>($"/api/v1/courses/{created.Id}");
            Assert.Equal("Before", read!.Title);

            var updatedResponse = await admin.PutAsJsonAsync(
                $"/api/v1/courses/{created.Id}",
                new { title = "After", credits = 4 });
            Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
            var updated = await updatedResponse.Content.ReadFromJsonAsync<CourseDto>();
            Assert.Equal("After", updated!.Title);

            var deleted = await admin.DeleteAsync($"/api/v1/courses/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/courses/{created.Id}")).StatusCode);
        });

    [Fact]
    public Task Anonymous_create_course_is_401()
        => WithFactoryAsync(async factory =>
        {
            var client = factory.CreateClient();
            var response = await client.PostAsJsonAsync("/api/v1/courses", new { code = "X1", title = "Nope", credits = 1 });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });

    [Fact]
    public Task Student_create_course_is_403()
        => WithFactoryAsync(async factory =>
        {
            var student = factory.CreateClient().AsTestUser("stu-2", "Student");
            var response = await student.PostAsJsonAsync("/api/v1/courses", new { code = "X2", title = "Nope", credits = 1 });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        });

    [Fact]
    public async Task Development_token_is_accepted_by_runtime_jwt_handler()
    {
        EnsurePg();
        await _fx.ResetAsync();
        await _fx.UsingJwtFactoryAsync(async factory =>
        {
            var client = factory.CreateClient();
            var tokenResponse = await client.PostAsJsonAsync(
                "/token/dev",
                new { sub = "runtime-admin", role = "Admin" });
            Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
            var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            var token = tokenPayload.GetProperty("access_token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var created = await client.PostAsJsonAsync(
                "/api/v1/courses",
                new { code = "JWT1", title = "Runtime JWT", credits = 3 });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        });
    }

    [Fact]
    public Task Student_cannot_enroll_or_list_as_another_student()
        => WithFactoryAsync(async factory =>
        {
            var admin = factory.CreateClient().AsTestUser("admin-owner", "Admin");
            var student = factory.CreateClient().AsTestUser("student-owner", "Student");
            var course = await (await admin.PostAsJsonAsync(
                    "/api/v1/courses",
                    new { code = "OWN1", title = "Ownership", credits = 2 }))
                .Content.ReadFromJsonAsync<CourseDto>();
            var section = await (await admin.PostAsJsonAsync(
                    "/api/v1/sections",
                    new { courseId = course!.Id, term = "2026F", capacity = 2 }))
                .Content.ReadFromJsonAsync<SectionDto>();

            var forbiddenEnroll = await student.PostAsJsonAsync(
                "/api/v1/enrollments",
                new CreateEnrollmentRequest(Guid.NewGuid(), section!.Id));
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenEnroll.StatusCode);

            var forbiddenList = await student.GetAsync($"/api/v1/enrollments?studentId={Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenList.StatusCode);
        });

    [Fact]
    public Task Respawn_isolates_tests_empty_list()
        => WithFactoryAsync(async factory =>
        {
            var student = factory.CreateClient().AsTestUser("stu-3", "Student");
            var list = await student.GetFromJsonAsync<List<EnrollmentDto>>("/api/v1/enrollments");
            Assert.NotNull(list);
            Assert.Empty(list);
        });

    [Fact]
    public Task Concurrent_enrollment_never_oversells_section()
        => WithFactoryAsync(async factory =>
        {
            var admin = factory.CreateClient().AsTestUser("admin-capacity", "Admin");
            var course = await (await admin.PostAsJsonAsync(
                    "/api/v1/courses",
                    new { code = "LOCK1", title = "Row Lock", credits = 2 }))
                .Content.ReadFromJsonAsync<CourseDto>();
            var section = await (await admin.PostAsJsonAsync(
                    "/api/v1/sections",
                    new { courseId = course!.Id, term = "2026F", capacity = 1 }))
                .Content.ReadFromJsonAsync<SectionDto>();

            var firstClient = factory.CreateClient().AsTestUser("concurrent-1", "Student");
            var secondClient = factory.CreateClient().AsTestUser("concurrent-2", "Student");
            var responses = await Task.WhenAll(
                firstClient.PostAsJsonAsync(
                    "/api/v1/enrollments",
                    new CreateEnrollmentRequest(Guid.Empty, section!.Id)),
                secondClient.PostAsJsonAsync(
                    "/api/v1/enrollments",
                    new CreateEnrollmentRequest(Guid.Empty, section.Id)));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            var enrollments = await Task.WhenAll(
                responses.Select(response => response.Content.ReadFromJsonAsync<EnrollmentDto>()));
            Assert.Single(enrollments, enrollment => enrollment!.Status == EnrollmentStatus.Confirmed);
            Assert.Single(enrollments, enrollment => enrollment!.Status == EnrollmentStatus.Waitlisted);
        });

    [Fact]
    public async Task Migrations_history_table_exists_after_startup()
    {
        EnsurePg();
        await using var conn = new Npgsql.NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Postgres folds unquoted identifiers to lowercase.
        cmd.CommandText = "SELECT COUNT(*) FROM __efmigrationshistory";
        try
        {
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
            Assert.True(count >= 1, $"expected at least 1 migration row, got {count}");
        }
        catch (Npgsql.NpgsqlException ex) when (ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            // Try quoted table name (EF creates it quoted).
            cmd.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"";
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
            Assert.True(count >= 1, $"expected at least 1 migration row, got {count}");
        }
    }
}
