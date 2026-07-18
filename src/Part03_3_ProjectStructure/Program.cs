// LearnAspNet
// Doc   : ASP.NetStudy/第3部分-3-项目结构-完整实施指南.md
// Part  : Part03_3 · ProjectStructure
// Title : 项目结构 · 分层骨架

using Part03_3.Application;
using Part03_3.Contracts;
using Part03_3.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPart03_3Application();
builder.Services.AddPart03_3Infrastructure();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part03_3_ProjectStructure",
    layers = new[] { "Contracts", "Domain", "Application", "Infrastructure", "Api(host)" },
    rule = "Application ↛ Infrastructure; Domain ↛ EF/ASP.NET; Contracts ↛ Domain; composition root wires all",
    deploy = "see deploy/docker|k8s|bicep stubs",
}));

app.MapGet("/api/v1/courses", async (ICourseRepository repo) =>
{
    var list = await repo.ListAsync();
    return Results.Ok(list.Select(c => new CourseResponse(c.Id, c.Code, c.Title, c.Credits)));
});

app.MapPost("/api/v1/courses", async (CreateCourseRequest dto, ICreateCourseHandler handler) =>
{
    var course = await handler.HandleAsync(dto.Code, dto.Title, dto.Credits);
    return Results.Created(
        $"/api/v1/courses/{course.Id}",
        new CourseResponse(course.Id, course.Code, course.Title, course.Credits));
});

app.Run();

public partial class Program;
