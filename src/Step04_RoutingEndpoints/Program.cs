// LearnAspNet
// Doc   : ASP.NetStudy/步骤4-路由与终结点-完整实施指南.md
// Part  : Step04 · RoutingEndpoints
// Title : 路由与终结点

using Campus.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<InMemoryCourseCatalog>();
// Register custom IRouteConstraint
builder.Services.Configure<RouteOptions>(o => o.ConstraintMap.Add("termcode", typeof(TermCodeConstraint)));

var app = builder.Build();

var catalog = app.Services.GetRequiredService<InMemoryCourseCatalog>();
catalog.Seed();

app.MapGet("/", () => Results.Ok(new { lab = "Step04_RoutingEndpoints" }));

var api = app.MapGroup("/api/v1").WithTags("Campus");

api.MapGet("/courses", (InMemoryCourseCatalog c) => Results.Ok(c.List()))
    .WithName("ListCourses");

api.MapGet("/courses/{id:guid}", (Guid id, InMemoryCourseCatalog c) =>
    {
        var course = c.Get(id);
        return course is null ? Results.NotFound(new { errorCode = ErrorCodes.NotFound }) : Results.Ok(course);
    })
    .WithName("GetCourseById");

api.MapGet("/courses/by-code/{code:regex(^[A-Z]{{2,8}}$)}", (string code, InMemoryCourseCatalog c) =>
    {
        var course = c.FindByCode(code);
        return course is null ? Results.NotFound() : Results.Ok(course);
    })
    .WithName("GetCourseByCode");

// Custom IRouteConstraint: term codes like 2026S1, 2026F, 2027S2
api.MapGet("/sections/{term:termcode}", (string term) => Results.Ok(new { term, valid = true }))
    .WithName("GetSectionByTerm");

// Nested MapGroup + group-level filter (demonstrates inheritance)
var admin = api.MapGroup("/admin")
    .WithTags("Admin")
    .AddEndpointFilter(async (ctx, next) =>
    {
        ctx.HttpContext.Response.Headers["X-Admin-Filter"] = "applied";
        return await next(ctx);
    });

admin.MapGet("/stats", (InMemoryCourseCatalog c) => Results.Ok(new { totalCourses = c.List().Count }))
    .WithName("AdminStats");

app.MapGet("/links/course/{id:guid}", (Guid id, LinkGenerator links, HttpContext http) =>
{
    var href = links.GetUriByName(http, "GetCourseById", new { id });
    return Results.Ok(new { href });
});

app.Run();

public partial class Program;

public sealed class InMemoryCourseCatalog
{
    private readonly Dictionary<Guid, CourseDto> _courses = new();

    public void Seed()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _courses[id] = new CourseDto(id, "CS101", "Intro to Computing", 3);
    }

    public IReadOnlyCollection<CourseDto> List() => _courses.Values.ToList();

    public CourseDto? Get(Guid id) => _courses.GetValueOrDefault(id);

    public CourseDto? FindByCode(string code) =>
        _courses.Values.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Custom IRouteConstraint: matches term codes like 2026S1, 2026F, 2027S2 (YYYY + S/F + digit).</summary>
public sealed class TermCodeConstraint : IRouteConstraint
{
    private static readonly System.Text.RegularExpressions.Regex Pattern =
        new(@"^\d{4}[SFsf]\d$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public bool Match(
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection)
    {
        if (values.TryGetValue(routeKey, out var value) && value is string s)
        {
            return Pattern.IsMatch(s);
        }

        return false;
    }
}