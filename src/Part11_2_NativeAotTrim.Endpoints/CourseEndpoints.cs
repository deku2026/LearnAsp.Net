using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Part11_2_NativeAotTrim.Endpoints;

public static class CourseEndpoints
{
    public static IEndpointRouteBuilder MapCourseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/courses");
        group.MapGet("/{code}", GetCourseByCode);
        return app;
    }

    private static IResult GetCourseByCode(string code)
    {
        var course = CourseCatalog.Find(code);
        return course is null
            ? Results.NotFound(new CourseNotFoundResponse(code, "course.not_found"))
            : Results.Ok(course);
    }
}

public sealed record CourseDto(string Code, string Title, int Credits);

public sealed record CourseNotFoundResponse(string Code, string Error);

public static class CourseCatalog
{
    private static readonly IReadOnlyDictionary<string, CourseDto> Courses =
        new Dictionary<string, CourseDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["CS-1010"] = new("CS-1010", "Intro to Computer Science", 4),
            ["CS-2020"] = new("CS-2020", "Data Structures", 4),
            ["PHYS-2200"] = new("PHYS-2200", "Classical Mechanics", 3),
            ["MATH-1100"] = new("MATH-1100", "Calculus I", 4),
        };

    public static CourseDto? Find(string code) =>
        Courses.TryGetValue(code, out var c) ? c : null;
}