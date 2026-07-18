// LearnAspNet
// Doc   : ASP.NetStudy/步骤5-MinimalAPI与Controller-完整实施指南.md
// Part  : Step05 · MinimalApiVsController
// Title : MinimalAPI 与 Controller

using System.Runtime.CompilerServices;
using Campus.Contracts;
using Step05_MinimalApiVsController;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<CampusStore>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { lab = "Step05_MinimalApiVsController" }));

var api = app.MapGroup("/api/v1");

api.MapGet("/courses", (string? q, CampusStore store) => Results.Ok(store.ListCourses(q)));
api.MapGet("/courses/{id:guid}", (Guid id, CampusStore store) =>
{
    var course = store.GetCourse(id);
    return course is null ? Results.NotFound(new { errorCode = ErrorCodes.NotFound }) : Results.Ok(course);
});
api.MapPost("/courses", (CreateCourseRequest request, CampusStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title) || request.Credits < 1)
    {
        return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed });
    }

    var created = store.AddCourse(request);
    return Results.Created($"/api/v1/courses/{created.Id}", created);
}).AddEndpointFilter(async (ctx, next) =>
{
    if (ctx.Arguments.OfType<CreateCourseRequest>().FirstOrDefault() is { Code: var code } &&
        string.Equals(code, "BLOCKED", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(new { errorCode = ErrorCodes.ValidationFailed, detail = "blocked by filter" }, statusCode: 400);
    }

    return await next(ctx);
});

api.MapGet("/sections", (CampusStore store) => Results.Ok(store.ListSections()));
api.MapPost("/sections", (CreateSectionRequest request, CampusStore store) =>
{
    try
    {
        var section = store.AddSection(request);
        return Results.Created($"/api/v1/sections/{section.Id}", section);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { errorCode = ErrorCodes.NotFound });
    }
});

api.MapGet("/enrollments", (Guid? studentId, CampusStore store) => Results.Ok(store.ListEnrollments(studentId)));
api.MapPost("/enrollments", (CreateEnrollmentRequest request, CampusStore store) =>
{
    try
    {
        var enrollment = store.Enroll(request);
        return Results.Created($"/api/v1/enrollments/{enrollment.Id}", enrollment);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { errorCode = ErrorCodes.NotFound });
    }
    catch (InvalidOperationException ex) when (ex.Message == ErrorCodes.EnrollmentDuplicate)
    {
        return Results.Conflict(new { errorCode = ErrorCodes.EnrollmentDuplicate });
    }
});

api.MapPost("/enrollments/{id:guid}/cancel", (Guid id, CampusStore store) =>
{
    try
    {
        return Results.Ok(store.Cancel(id));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { errorCode = ErrorCodes.EnrollmentNotFound });
    }
});

api.MapGet("/enrollments/stream", async (HttpContext http, CampusStore store, CancellationToken ct) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    await foreach (var item in WatchEnrollments(store, ct))
    {
        await http.Response.WriteAsync($"data: {item.Id}:{item.Status}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
});

app.MapControllers();
app.Run();

static async IAsyncEnumerable<EnrollmentDto> WatchEnrollments(
    CampusStore store,
    [EnumeratorCancellation] CancellationToken ct)
{
    // Lab SSE: emit current snapshot then a short heartbeat-friendly close after few polls
    for (var i = 0; i < 5 && !ct.IsCancellationRequested; i++)
    {
        foreach (var e in store.ListEnrollments())
        {
            yield return e;
        }

        await Task.Delay(50, ct);
    }
}

public partial class Program;
