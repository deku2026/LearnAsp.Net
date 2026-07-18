// LearnAspNet
// Doc   : ASP.NetStudy/第3部分-2-应用架构-完整实施指南.md
// Part  : Part03_2 · AppArchitecture
// Title : 应用架构 · 模块化单体

using Part03_2_AppArchitecture.Modules.Catalog;
using Part03_2_AppArchitecture.Modules.Enrollment;
using Part03_2_AppArchitecture.Modules.Notices;
using Part03_2_AppArchitecture.Outbox;

using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<CatalogModule>();
builder.Services.AddSingleton<ICatalogModule>(sp => sp.GetRequiredService<CatalogModule>());
builder.Services.AddSingleton<InMemoryOutbox>();
builder.Services.AddSingleton<IOutbox>(sp => sp.GetRequiredService<InMemoryOutbox>());
builder.Services.AddSingleton<NoticesModule>();
builder.Services.AddSingleton<INoticesModule>(sp => sp.GetRequiredService<NoticesModule>());
builder.Services.AddSingleton<IEnrollmentModule, EnrollmentModule>();
builder.Services.AddHostedService<OutboxProcessor>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part03_2_AppArchitecture",
    modules = new[] { "Catalog", "Enrollment", "Notices" },
    communication = new[] { "PublicApi sync seat reserve", "Outbox async EnrollmentConfirmed → Notices" },
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
