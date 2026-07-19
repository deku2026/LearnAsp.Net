using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Part04_3_MultiTenant.Tests;

[Collection("tenant")]
public sealed class MultiTenantTests
{
    private readonly TenantFixture _fx;

    public MultiTenantTests(TenantFixture fx) => _fx = fx;

    private void EnsurePg() => TenantSkip.IfNotAvailable(_fx);

    private static HttpClient CreateClientForTenant(WebApplicationFactory<Program> factory, string tenantId)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        return http;
    }

    [Fact]
    public async Task Tenant_a_sees_only_own_courses()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();

        var tenantA = CreateClientForTenant(factory, "college-a");
        var tenantB = CreateClientForTenant(factory, "college-b");

        // Create course as tenant A
        var r1 = await tenantA.PostAsJsonAsync("/api/v1/courses", new { code = "A101", title = "Course A", credits = 3 });
        r1.EnsureSuccessStatusCode();

        // Create course as tenant B
        var r2 = await tenantB.PostAsJsonAsync("/api/v1/courses", new { code = "B101", title = "Course B", credits = 2 });
        r2.EnsureSuccessStatusCode();

        // Tenant A lists — should only see A's course
        var listA = await tenantA.GetFromJsonAsync<JsonElement>("/api/v1/courses");
        var aCourses = listA.EnumerateArray().ToList();
        Assert.Single(aCourses);
        Assert.Equal("college-a", aCourses[0].GetProperty("collegeId").GetString());

        // Tenant B lists — should only see B's course
        var listB = await tenantB.GetFromJsonAsync<JsonElement>("/api/v1/courses");
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

        var tenantB = CreateClientForTenant(factory, "college-b");
        var r1 = await tenantB.PostAsJsonAsync("/api/v1/courses", new { code = "B201", title = "Private B", credits = 1 });
        var courseB = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var idB = courseB.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await tenantB.GetAsync($"/api/v1/courses/{idB}")).StatusCode);

        // Tenant B warmed the cache first. Tenant A still gets 404 because both
        // the query filter and cache key include the tenant.
        var tenantA = CreateClientForTenant(factory, "college-a");
        var r2 = await tenantA.GetAsync($"/api/v1/courses/{idB}");
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);
    }

    [Fact]
    public async Task SaveChanges_auto_stamps_college_id()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();

        var http = CreateClientForTenant(factory, "college-auto");
        // Create without specifying CollegeId — SaveChanges interceptor stamps it
        var r = await http.PostAsJsonAsync("/api/v1/courses", new { code = "AUTO1", title = "Auto Stamp", credits = 1 });
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

        var tenantA = CreateClientForTenant(factory, "college-a");

        // Create + soft-delete as tenant A
        var r1 = await tenantA.PostAsJsonAsync("/api/v1/courses", new { code = "DEL1", title = "To Delete", credits = 1 });
        var course = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var id = course.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await tenantA.GetAsync($"/api/v1/courses/{id}")).StatusCode);
        await tenantA.DeleteAsync($"/api/v1/courses/{id}");
        Assert.Equal(HttpStatusCode.NotFound, (await tenantA.GetAsync($"/api/v1/courses/{id}")).StatusCode);

        // Admin view including deleted — should only see college-a's course (tenant filter active)
        var admin = await tenantA.GetFromJsonAsync<JsonElement>("/api/v1/courses/all-including-deleted");
        var allCourses = admin.GetProperty("courses").EnumerateArray().ToList();
        Assert.True(allCourses.Count >= 1);
        Assert.True(allCourses.All(c => c.GetProperty("collegeId").GetString() == "college-a"));
    }

    [Fact]
    public async Task Unknown_tenant_is_rejected_before_data_access()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();
        var unknown = CreateClientForTenant(factory, "college-does-not-exist");
        var response = await unknown.GetAsync("/api/v1/courses");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tenant.not_found", json.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task SaveChanges_rejects_modified_entity_from_another_tenant()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var setter = scope.ServiceProvider.GetRequiredService<ITenantSetter>();
        setter.SetTenant("college-a");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var foreign = new Course
        {
            Id = Guid.NewGuid(),
            Code = "B-ATTACHED",
            Title = "Foreign",
            Credits = 1,
            CollegeId = "college-b",
        };
        db.Attach(foreign);
        db.Entry(foreign).State = EntityState.Modified;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await db.SaveChangesAsync());
        Assert.Contains("禁止跨租户写入", error.Message, StringComparison.Ordinal);
    }
}
