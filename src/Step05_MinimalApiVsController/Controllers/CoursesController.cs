using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Campus.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Step05_MinimalApiVsController.Controllers;

[ApiController]
[Route("api/controller/v1/courses")]
public sealed class CoursesController(CampusStore store) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CourseDto>> List([FromQuery] string? q) => Ok(store.ListCourses(q));

    [HttpGet("{id:guid}")]
    public ActionResult<CourseDto> Get(Guid id)
    {
        var course = store.GetCourse(id);
        return course is null ? NotFound(new { errorCode = ErrorCodes.NotFound }) : Ok(course);
    }

    [HttpPost]
    public ActionResult<CourseDto> Create([FromBody] ControllerCreateCourseRequest request)
    {
        var created = store.AddCourse(new CreateCourseRequest(request.Code, request.Title, request.Credits));
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("throw")]
    public ActionResult Throw() => throw new InvalidOperationException("controller-lab-error");
}

public sealed record ControllerCreateCourseRequest(
    [Required, MinLength(2), MaxLength(16)] string Code,
    [Required, MinLength(2), MaxLength(200)] string Title,
    [Range(1, 10)] int Credits);

public sealed class ControllerResourceFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        context.HttpContext.Response.Headers["X-Controller-Resource-Filter"] = "applied";
        await next();
    }
}

public sealed class ControllerTimingFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        await next();
        context.HttpContext.Response.Headers["X-Controller-Elapsed-ms"] =
            stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class ControllerExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not InvalidOperationException)
        {
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Title = "Controller operation failed",
            Status = StatusCodes.Status500InternalServerError,
            Extensions = { ["errorCode"] = ErrorCodes.InternalError },
        })
        {
            StatusCode = StatusCodes.Status500InternalServerError,
        };
        context.ExceptionHandled = true;
    }
}
