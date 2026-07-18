// LearnAspNet
// Doc   : ASP.NetStudy/步骤5-MinimalAPI与Controller-完整实施指南.md
// Part  : Step05 · MinimalApiVsController
// Title : MinimalAPI 与 Controller

using System.Runtime.CompilerServices;
using Campus.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Step05_MinimalApiVsController;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<CampusStore>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { lab = "Step05_MinimalApiVsController" }));

// Extension-method endpoint organization (not all inline in Program.cs).
app.MapCampusApi();

app.MapControllers();
app.Run();

public partial class Program;

/// <summary>Extension methods organizing Campus endpoints via MapGroup.</summary>
public static class CampusApiExtensions
{
    public static IEndpointRouteBuilder MapCampusApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        // [AsParameters] record packing for query binding
        api.MapGet("/courses", ([AsParameters] ListCoursesQuery query, CampusStore store) =>
            Results.Ok(store.ListCourses(query.Q)));

        // TypedResults + Results<,> union for compile-time OpenAPI precision
        api.MapGet("/courses/{id:guid}", Results<Ok<CourseDto>, NotFound> (Guid id, CampusStore store) =>
        {
            var course = store.GetCourse(id);
            return course is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(course);
        });

        // Custom TryParse binding for CourseCode struct
        api.MapGet("/courses/by-code/{code}", (CourseCode code, CampusStore store) =>
        {
            var course = store.FindByCode(code.Value);
            return course is null
                ? Results.NotFound(new { errorCode = ErrorCodes.NotFound })
                : Results.Ok(course);
        });

        api.MapPost("/courses", Results<Created<CourseDto>, BadRequest<ProblemDetails>> ([FromBody] CreateCourseRequest request, CampusStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title) || request.Credits < 1)
            {
                return TypedResults.BadRequest(new ProblemDetails
                {
                    Title = "Validation failed",
                    Status = 400,
                    Extensions = { ["errorCode"] = ErrorCodes.ValidationFailed },
                });
            }

            var created = store.AddCourse(request);
            return TypedResults.Created($"/api/v1/courses/{created.Id}", created);
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
        api.MapPost("/sections", ([FromBody] CreateSectionRequest request, CampusStore store) =>
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
        api.MapPost("/enrollments", ([FromBody] CreateEnrollmentRequest request, CampusStore store) =>
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

        // SSE via TypedResults.ServerSentEvents (.NET 10)
        api.MapGet("/enrollments/stream", async (CampusStore store, CancellationToken ct) =>
        {
            return TypedResults.ServerSentEvents(WatchEnrollments(store, ct));
        });

        return app;
    }

    private static async IAsyncEnumerable<EnrollmentDto> WatchEnrollments(
        CampusStore store,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < 5 && !ct.IsCancellationRequested; i++)
        {
            foreach (var e in store.ListEnrollments())
            {
                yield return e;
            }

            await Task.Delay(50, ct);
        }
    }
}

/// <summary>[AsParameters] query record — packs multiple query params into one bindable type.</summary>
public sealed record ListCoursesQuery(string? Q);

/// <summary>Custom type with TryParse for route binding: course code like CS101.</summary>
public readonly struct CourseCode
{
    public string Value { get; }

    private CourseCode(string value) => Value = value;

    public static bool TryParse(string? input, out CourseCode result)
    {
        if (input is not null &&
            input.Length is >= 2 and <= 16 &&
            input.All(c => char.IsLetterOrDigit(c)))
        {
            result = new CourseCode(input);
            return true;
        }

        result = default;
        return false;
    }

    public override string ToString() => Value;
}
