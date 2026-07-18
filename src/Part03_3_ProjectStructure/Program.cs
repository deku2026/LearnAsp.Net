// LearnAspNet
// Doc   : ASP.NetStudy/第3部分-3-项目结构-完整实施指南.md
// Part  : Part03_3 · ProjectStructure
// Title : 项目结构 · 分层骨架

using Part03_3.Application;
using Part03_3.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Composition root only: Application + Infrastructure registration
builder.Services.AddPart03_3Application();
builder.Services.AddPart03_3Infrastructure();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part03_3_ProjectStructure",
    layers = new[] { "Domain", "Application", "Infrastructure", "Api(host)" },
    rule = "Application ↛ Infrastructure; Domain ↛ EF/ASP.NET; composition root wires all",
}));

app.MapGet("/api/v1/courses", async (ICourseRepository repo) => Results.Ok(await repo.ListAsync()));

app.MapPost("/api/v1/courses", async (CreateCourseDto dto, ICreateCourseHandler handler) =>
{
    var course = await handler.HandleAsync(dto.Code, dto.Title, dto.Credits);
    return Results.Created($"/api/v1/courses/{course.Id}", new
    {
        course.Id,
        course.Code,
        course.Title,
        course.Credits,
    });
});

app.Run();

public partial class Program;

public sealed record CreateCourseDto(string Code, string Title, int Credits);
