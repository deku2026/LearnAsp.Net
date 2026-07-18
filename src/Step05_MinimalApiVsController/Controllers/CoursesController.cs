using Campus.Contracts;
using Microsoft.AspNetCore.Mvc;

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
    public ActionResult<CourseDto> Create([FromBody] CreateCourseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title) || request.Credits < 1)
        {
            return BadRequest(new { errorCode = ErrorCodes.ValidationFailed });
        }

        var created = store.AddCourse(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
