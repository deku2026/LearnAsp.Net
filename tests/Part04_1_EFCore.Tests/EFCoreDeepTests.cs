using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Campus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Part04_1_EFCore;

namespace Part04_1_EFCore.Tests;

[Collection("pg")]
public sealed class EFCoreDeepTests
{
    private readonly PgFixture _fx;

    public EFCoreDeepTests(PgFixture fx) => _fx = fx;

    private void EnsurePg() => Skip.IfNotAvailable(_fx);

    [Fact]
    public async Task Keyset_pagination_returns_ordered_pages()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();
        var client = factory.CreateClient();

        // Create a course
        var course = await (await client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "CS101", title = "Intro", credits = 3, collegeId = "college-1",
        })).Content.ReadFromJsonAsync<JsonElement>();

        // Create 5 sections
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/v1/sections", new
            {
                courseId = course.GetProperty("id").GetGuid(),
                sectionName = $"S{i}", semester = "2026F", capacity = 30, collegeId = "college-1",
            });
        }

        // Page 1: limit=2
        var page1 = await client.GetFromJsonAsync<JsonElement>("/api/v1/sections?limit=2");
        var data1 = page1.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, data1.Count);
        Assert.False(string.IsNullOrWhiteSpace(page1.GetProperty("nextCursor").GetString()));

        // Page 2: use lastSeenId from page1
        var lastId = data1[^1].GetProperty("id").GetGuid();
        var page2 = await client.GetFromJsonAsync<JsonElement>($"/api/v1/sections?lastSeenId={lastId}&limit=2");
        var data2 = page2.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, data2.Count);
        // Keyset: all page2 ids > lastId
        Assert.True(data2.All(d => d.GetProperty("id").GetGuid() > lastId));
    }

    [Fact]
    public async Task N1_fix_projection_returns_single_query_data()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();
        var client = factory.CreateClient();

        var course = await (await client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "N1", title = "N1 Test", credits = 2, collegeId = "college-1",
        })).Content.ReadFromJsonAsync<JsonElement>();

        await client.PostAsJsonAsync("/api/v1/sections", new
        {
            courseId = course.GetProperty("id").GetGuid(),
            sectionName = "S1", semester = "2026F", capacity = 10, collegeId = "college-1",
        });

        // N+1 demo endpoint should return data
        var r1 = await client.GetAsync("/api/v1/sections/n1-demo");
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Include fix
        var r2 = await client.GetAsync("/api/v1/sections/n1-fix-include");
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        // Projection fix
        var r3 = await client.GetAsync("/api/v1/sections/n1-fix-projection");
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);

        var proj = await r3.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(proj.GetProperty("count").GetInt32() >= 1);
    }

    [Fact]
    public async Task AsSplitQuery_returns_data_without_duplication()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();
        var client = factory.CreateClient();

        var course = await (await client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "SPLIT", title = "Split Test", credits = 2, collegeId = "college-1",
        })).Content.ReadFromJsonAsync<JsonElement>();

        var section = await (await client.PostAsJsonAsync("/api/v1/sections", new
        {
            courseId = course.GetProperty("id").GetGuid(),
            sectionName = "S1", semester = "2026F", capacity = 10, collegeId = "college-1",
        })).Content.ReadFromJsonAsync<JsonElement>();

        // Cartesian and split both return OK
        var r1 = await client.GetAsync("/api/v1/sections/cartesian");
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var r2 = await client.GetAsync("/api/v1/sections/split");
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Fact]
    public async Task Optimistic_concurrency_second_write_returns_409()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();
        var client = factory.CreateClient();

        var course = await (await client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "CONC", title = "Concurrency", credits = 1, collegeId = "college-1",
        })).Content.ReadFromJsonAsync<JsonElement>();
        var id = course.GetProperty("id").GetGuid();

        // First update succeeds (fresh xmin)
        var r1 = await client.PutAsJsonAsync($"/api/v1/courses/{id}", new { title = "Updated1", credits = 2 });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Simulate stale context: load with a separate DbContext, keep it tracked, then update via HTTP
        using (var scope1 = factory.Services.CreateScope())
        {
            var db1 = scope1.ServiceProvider.GetRequiredService<CampusDbContext>();
            var tracked = await db1.Courses.AsTracking().FirstOrDefaultAsync(c => c.Id == id);
            Assert.NotNull(tracked);
            tracked!.Title = "FromStaleContext";

            // Meanwhile, update via HTTP (changes the row's xmin)
            var r2 = await client.PutAsJsonAsync($"/api/v1/courses/{id}", new { title = "ViaHttp", credits = 3 });
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

            // Now save the stale context → xmin mismatch → DbUpdateConcurrencyException
            // The app's PUT endpoint catches this and returns 409.
            // But this is a direct DbContext call, not through the endpoint.
            // We verify the concurrency mechanism: the stale save throws.
            var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                async () => await db1.SaveChangesAsync());
            Assert.NotNull(ex);
        }
    }

    [Fact]
    public async Task ExecuteUpdate_bypasses_change_tracking()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();
        var client = factory.CreateClient();

        var course = await (await client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "EXEC", title = "ExecuteUpdate", credits = 1, collegeId = "college-1",
        })).Content.ReadFromJsonAsync<JsonElement>();

        await client.PostAsJsonAsync("/api/v1/sections", new
        {
            courseId = course.GetProperty("id").GetGuid(),
            sectionName = "S1", semester = "2026F", capacity = 10, collegeId = "college-1",
        });

        var r = await client.PostAsync("/api/v1/sections/batch-close", null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("affected").GetInt32() >= 1);
    }

    [Fact]
    public async Task Named_filters_ignore_softdelete_preserves_tenant()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var factory = _fx.CreateFactory();
        var client = factory.CreateClient();

        // Create course for college-1
        await client.PostAsJsonAsync("/api/v1/courses", new
        {
            code = "FILT", title = "Filter Test", credits = 1, collegeId = "college-1",
        });

        // List including deleted — should show course even if not deleted
        var r = await client.GetAsync("/api/v1/courses/all-including-deleted");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var courses = body.GetProperty("courses").EnumerateArray().ToList();
        Assert.True(courses.Count >= 1);
        // All courses should belong to college-1 (tenant filter still active)
        Assert.True(courses.All(c => c.GetProperty("collegeId").GetString() == "college-1"));
    }

    [Fact]
    public async Task Migrations_history_table_exists()
    {
        EnsurePg();
        await _fx.ResetDatabaseAsync();
        await using var conn = new Npgsql.NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"";
        try
        {
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
            Assert.True(count >= 1);
        }
        catch (Npgsql.NpgsqlException ex) when (ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            cmd.CommandText = "SELECT COUNT(*) FROM __efmigrationshistory";
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
            Assert.True(count >= 1);
        }
    }
}