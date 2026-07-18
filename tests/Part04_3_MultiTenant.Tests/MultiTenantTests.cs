using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Part04_3_MultiTenant;

namespace Part04_3_MultiTenant.Tests;

[Collection("tenant")]
public sealed class MultiTenantTests
{
    private readonly TenantFixture _fx;

    public MultiTenantTests(TenantFixture fx) => _fx = fx;

    private void EnsurePg() => TenantSkip.IfNotAvailable(_fx);

    private HttpClient CreateClientForTenant(WebApplicationFactory<Program> factory, string tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        return client;
    }

    [Fact]
    public async Task Tenant_a_sees_only_own_courses()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();

        var clientA = CreateClientForTenant(factory, "college-a");
        var clientB = CreateClientForTenant(factory, "college-b");

        // Create course as tenant A
        var r1 = await clientA.PostAsJsonAsync("/api/v1/courses", new { code = "A101", title = "Course A", credits = 3 });
        r1.EnsureSuccessStatusCode();

        // Create course as tenant B
        var r2 = await clientB.PostAsJsonAsync("/api/v1/courses", new { code = "B101", title = "Course B", credits = 2 });
        r2.EnsureSuccessStatusCode();

        // Tenant A lists — should only see A's course
        var listA = await clientA.GetFromJsonAsync<JsonElement>("/api/v1/courses");
        var aCourses = listA.EnumerateArray().ToList();
        Assert.Single(aCourses);
        Assert.Equal("college-a", aCourses[0].GetProperty("collegeId").GetString());

        // Tenant B lists — should only see B's course
        var listB = await clientB.GetFromJsonAsync<JsonElement>("/api/v1/courses");
        var bCourses = listB.EnumerateArray().ToList();
        Assert.Single(bCourses);
        Assert.Equal("college-b", bCourses[0].GetProperty("collegeId").GetString());
    }

    [Fact]
    public async Task Tenant_a_access_tenant_b_course_returns_404()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();

        var clientB = CreateClientForTenant(factory, "college-b");
        var r1 = await clientB.PostAsJsonAsync("/api/v1/courses", new { code = "B201", title = "Private B", credits = 1 });
        var courseB = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var idB = courseB.GetProperty("id").GetGuid();

        // Tenant A tries to access B's course → 404 (query filter hides it)
        var clientA = CreateClientForTenant(factory, "college-a");
        var r2 = await clientA.GetAsync($"/api/v1/courses/{idB}");
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);
    }

    [Fact]
    public async Task SaveChanges_auto_stamps_college_id()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();

        var client = CreateClientForTenant(factory, "college-auto");
        // Create without specifying CollegeId — SaveChanges interceptor stamps it
        var r = await client.PostAsJsonAsync("/api/v1/courses", new { code = "AUTO1", title = "Auto Stamp", credits = 1 });
        r.EnsureSuccessStatusCode();
        var course = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("college-auto", course.GetProperty("collegeId").GetString());
    }

    [Fact]
    public async Task Named_filters_ignore_softdelete_preserves_tenant()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();

        var clientA = CreateClientForTenant(factory, "college-a");
        var clientB = CreateClientForTenant(factory, "college-b");

        // Create + soft-delete as A
        var r1 = await clientA.PostAsJsonAsync("/api/v1/courses", new { code = "DEL1", title = "To Delete", credits = 1 });
        var course = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var id = course.GetProperty("id").GetGuid();

        await clientA.DeleteAsync($"/api/v1/courses/{id}");

        // Admin view including deleted — should only see college-a's course (tenant filter active)
        var admin = await clientA.GetFromJsonAsync<JsonElement>("/api/v1/courses/all-including-deleted");
        var allCourses = admin.GetProperty("courses").EnumerateArray().ToList();
        Assert.True(allCourses.Count >= 1);
        Assert.True(allCourses.All(c => c.GetProperty("collegeId").GetString() == "college-a"));
    }
}