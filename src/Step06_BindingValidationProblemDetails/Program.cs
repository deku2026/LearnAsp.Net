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
                : ctx.ProblemDetails.Status switch
                {
                    StatusCodes.Status404NotFound => ErrorCodes.NotFound,
                    >= StatusCodes.Status500InternalServerError => ErrorCodes.InternalError,
                    _ => ErrorCodes.ValidationFailed,
                };
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Instance = ctx.HttpContext.Request.Path;
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

api.MapPost("/sections", Results<Created<SectionDto>, NotFound<ProblemDetails>> (
    [FromBody] CreateSectionBody body,
    CourseBook book) =>
{
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
}).AddEndpointFilter<FluentValidationFilter<CreateSectionBody>>();

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
    private readonly object _stateLock = new();
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
        lock (_stateLock)
        {
            if (!_courses.ContainsKey(courseId))
            {
                throw new KeyNotFoundException();
            }

            if (_sections.Values.Any(section =>
                    section.CourseId == courseId &&
                    string.Equals(section.Term, term, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("A section already exists for this course and term.");
            }

            var dto = new SectionDto(Guid.NewGuid(), courseId, term.Trim(), capacity, capacity);
            _sections[dto.Id] = dto;
            return dto;
        }
    }

    public Task<bool> IsSectionUniqueAsync(Guid courseId, string term, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unique = !_sections.Values.Any(section =>
            section.CourseId == courseId &&
            string.Equals(section.Term, term, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(unique);
    }
}

/// <summary>IExceptionHandler: maps exceptions to ProblemDetails with stable errorCode.</summary>
public sealed class LabExceptionHandler(IHostEnvironment env, IProblemDetailsService problemDetailsService)
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
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Type = $"https://httpstatuses.com/{status}",
                Title = title,
                Status = status,
                Detail = env.IsDevelopment() ? exception.Message : null,
            },
        });
        return true;
    }
}

/// <summary>Runs FluentValidation asynchronously as an endpoint filter.</summary>
public sealed class FluentValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
    where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var model = context.Arguments.OfType<T>().FirstOrDefault();
        if (model is null)
        {
            return await next(context);
        }

        var result = await validator.ValidateAsync(model, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());
        var errorCode = result.Errors.FirstOrDefault()?.ErrorCode ?? ErrorCodes.ValidationFailed;
        context.HttpContext.Items["errorCode"] = errorCode;
        return TypedResults.ValidationProblem(errors, extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["traceId"] = context.HttpContext.TraceIdentifier,
        });
    }
}
