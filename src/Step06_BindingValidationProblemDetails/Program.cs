// LearnAspNet
// Doc   : ASP.NetStudy/步骤6-模型绑定-校验-ProblemDetails-完整实施指南.md
// Part  : Step06 · BindingValidationProblemDetails
// Title : 模型绑定 · 校验 · ProblemDetails

using System.Collections.Concurrent;
using Campus.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Step06_BindingValidationProblemDetails;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["errorCode"] =
            ctx.HttpContext.Items.TryGetValue("errorCode", out var code) && code is string s
                ? s
                : ErrorCodes.ValidationFailed;
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddValidatorsFromAssemblyContaining<CreateSectionBodyValidator>();
builder.Services.AddExceptionHandler<LabExceptionHandler>();
builder.Services.AddSingleton<CourseBook>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/", () => Results.Ok(new { lab = "Step06_BindingValidationProblemDetails" }));

var api = app.MapGroup("/api/v1");

// Built-in validation (DataAnnotations + IValidatableObject + custom attribute) via AddValidation
// Requires InterceptorsNamespaces in csproj — otherwise silent no-op.
api.MapPost("/courses", ([FromBody] CreateCourseBody body, CourseBook book) =>
{
    // Built-in validation runs automatically via AddValidation() interceptors.
    var created = book.Add(body.Code, body.Title, body.Credits);
    return Results.Created($"/api/v1/courses/{created.Id}", created);
});

api.MapPost("/sections", async Task<Results<Created<SectionDto>, ValidationProblem, NotFound<ProblemDetails>>> (
    [FromBody] CreateSectionBody body,
    IValidator<CreateSectionBody> validator,
    CourseBook book,
    HttpContext http) =>
{
    var result = await validator.ValidateAsync(body);
    if (!result.IsValid)
    {
        http.Items["errorCode"] = result.Errors.FirstOrDefault()?.ErrorCode ?? ErrorCodes.ValidationFailed;
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return TypedResults.ValidationProblem(errors, extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = http.Items["errorCode"],
        });
    }

    try
    {
        var section = book.AddSection(body.CourseId, body.Term, body.Capacity);
        return TypedResults.Created($"/api/v1/sections/{section.Id}", section);
    }
    catch (KeyNotFoundException)
    {
        return TypedResults.NotFound(new ProblemDetails
        {
            Title = "Course not found",
            Status = StatusCodes.Status404NotFound,
            Extensions = { ["errorCode"] = ErrorCodes.NotFound },
        });
    }
});

api.MapGet("/courses/{id:guid}", (Guid id, CourseBook book) =>
{
    var c = book.Get(id);
    return c is null
        ? Results.NotFound(new ProblemDetails
        {
            Title = "Not found",
            Status = 404,
            Extensions = { ["errorCode"] = ErrorCodes.NotFound },
        })
        : Results.Ok(c);
});

// Endpoint that throws to demonstrate IExceptionHandler → ProblemDetails
api.MapGet("/throw/{kind}", (string kind) =>
{
    throw kind switch
    {
        "notfound" => new KeyNotFoundException("resource"),
        "badarg" => new ArgumentException("bad input"),
        _ => new InvalidOperationException("boom"),
    };
});

app.Run();

public partial class Program;

public sealed class CourseBook
{
    private readonly ConcurrentDictionary<Guid, CourseDto> _courses = new();
    private readonly ConcurrentDictionary<Guid, SectionDto> _sections = new();

    public CourseDto Add(string code, string title, int credits)
    {
        var dto = new CourseDto(Guid.NewGuid(), code.Trim(), title.Trim(), credits);
        _courses[dto.Id] = dto;
        return dto;
    }

    public CourseDto? Get(Guid id) => _courses.GetValueOrDefault(id);

    public SectionDto AddSection(Guid courseId, string term, int capacity)
    {
        if (!_courses.ContainsKey(courseId))
        {
            throw new KeyNotFoundException();
        }

        var dto = new SectionDto(Guid.NewGuid(), courseId, term.Trim(), capacity, capacity);
        _sections[dto.Id] = dto;
        return dto;
    }
}

/// <summary>IExceptionHandler: maps exceptions to ProblemDetails with stable errorCode.</summary>
public sealed class LabExceptionHandler(IHostEnvironment env)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, errorCode, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Not found"),
            ArgumentException => (StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Bad request"),
            _ => (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "Internal error"),
        };

        httpContext.Items["errorCode"] = errorCode;
        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail = env.IsDevelopment() ? exception.Message : null,
            errorCode,
            traceId = httpContext.TraceIdentifier,
        };

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}