// LearnAspNet
// Doc   : ASP.NetStudy/第3部分-2-应用架构-完整实施指南.md
// Part  : Part03_2 · AppArchitecture
// Title : 应用架构 · 模块化单体

using System.Text.Json.Serialization;
using Part03_2.BuildingBlocks;
using Part03_2.Catalog;
using Part03_2.Catalog.Contracts;
using Part03_2.Enrollment;
using Part03_2.Enrollment.Contracts;
using Part03_2.Notices;
using Part03_2.Notices.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Composition root: modules only via PublicApi contracts + DI extensions
builder.Services.AddInProcessOutbox();
builder.Services.AddCatalogModule();
builder.Services.AddEnrollmentModule();
builder.Services.AddNoticesModule();

var app = builder.Build();

// Ensure module databases (in-memory schemas) are created
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<NoticesDbContext>().Database.EnsureCreatedAsync();
}

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part03_2_AppArchitecture",
    modules = new[]
    {
        "Part03_2.Catalog (+ Contracts)",
        "Part03_2.Enrollment (+ Contracts) → depends only Catalog.Contracts",
        "Part03_2.Notices (+ Contracts) → consumes EnrollmentConfirmed via Outbox",
    },
    dataIsolation = "separate EF InMemory databases: part03_2_catalog | part03_2_enrollment | part03_2_notices",
    communication = new[] { "sync PublicApi seat reserve", "async Outbox EnrollmentConfirmed → Notices" },
}));

var api = app.MapGroup("/api/v1");

api.MapPost("/courses", async (CreateCourseReq req, ICatalogModule catalog) =>
{
    var c = await catalog.CreateCourseAsync(req.Code, req.Title, req.Credits);
    return Results.Created($"/api/v1/courses/{c.Id}", c);
});

api.MapPost("/sections", async (CreateSectionReq req, ICatalogModule catalog) =>
{
    try
    {
        var s = await catalog.CreateSectionAsync(req.CourseId, req.Term, req.Capacity);
        return Results.Created($"/api/v1/sections/{s.Id}", s);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
});

api.MapPost("/enrollments", async (CreateEnrollmentReq req, IEnrollmentModule enrollment) =>
{
    try
    {
        var e = await enrollment.EnrollAsync(req.StudentId, req.SectionId);
        return Results.Created($"/api/v1/enrollments/{e.Id}", e);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
});

api.MapGet("/enrollments", async (Guid? studentId, IEnrollmentModule enrollment) =>
    Results.Ok(await enrollment.ListAsync(studentId)));

api.MapGet("/notices", async (INoticesModule notices) => Results.Ok(await notices.ListAsync()));

app.Run();

public partial class Program;

public sealed record CreateCourseReq(string Code, string Title, int Credits);
public sealed record CreateSectionReq(Guid CourseId, string Term, int Capacity);
public sealed record CreateEnrollmentReq(Guid StudentId, Guid SectionId);
