// LearnAspNet
// Doc   : ASP.NetStudy/第3部分-1-生产API设计-完整实施指南.md
// Part  : Part03_1 · ApiDesign
// Title : 生产 API 设计

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Campus.Contracts;
using Microsoft.AspNetCore.Mvc;
using Part03_1_ApiDesign;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CampusStore>();
builder.Services.AddProblemDetails(o =>
{
    o.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["errorCode"] =
            ctx.HttpContext.Items["errorCode"] as string ?? ErrorCodes.InternalError;
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;
    o.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddApiExplorer(o =>
{
    o.GroupNameFormat = "'v'VVV";
    o.SubstituteApiVersionInUrl = true;
});

builder.Services.AddOpenApi("v1");
builder.Services.AddOpenApi("v2");

var app = builder.Build();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapScalarApiReference(o => o.WithTitle("Campus API Design Lab"));

app.MapGet("/", () => Results.Ok(new { lab = "Part03_1_ApiDesign" }));

var versions = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0))
    .ReportApiVersions()
    .Build();

var v1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(versions)
    .MapToApiVersion(new ApiVersion(1, 0));

var v2 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(versions)
    .MapToApiVersion(new ApiVersion(2, 0));

// --- v1 courses ---
v1.MapGet("/courses", (string? q, string? after, int? limit, CampusStore store) =>
{
    var page = store.ListCourses(q, after, limit ?? 20, out var next);
    return Results.Ok(new
    {
        data = page.Select(MapCourseV1),
        nextCursor = next,
        hasMore = next is not null,
    });
});

v1.MapGet("/courses/{id:guid}", (Guid id, HttpContext http, CampusStore store) =>
{
    var course = store.GetCourse(id);
    if (course is null)
    {
        return NotFoundProblem();
    }

    var etag = ETag(course.RowVersion);
    if (http.Request.Headers.IfNoneMatch == etag)
    {
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    http.Response.Headers.ETag = etag;
    return Results.Ok(MapCourseV1(course));
});

v1.MapPost("/courses", (CreateCourseBody body, CampusStore store) =>
{
    if (!TryValidate(body, out var errors))
    {
        return ValidationProblem(errors);
    }

    var created = store.AddCourse(body.Code, body.Title, body.Credits);
    return Results.Created($"/api/v1/courses/{created.Id}", MapCourseV1(created));
});

v1.MapPut("/courses/{id:guid}", (Guid id, UpdateCourseBody body, HttpContext http, CampusStore store) =>
{
    if (!TryValidate(body, out var errors))
    {
        return ValidationProblem(errors);
    }

    if (!TryReadIfMatch(http, out var version, out var precondition))
    {
        return precondition!;
    }

    try
    {
        var updated = store.UpdateCourse(id, body.Title, body.Credits, version);
        if (updated is null)
        {
            return NotFoundProblem();
        }

        http.Response.Headers.ETag = ETag(updated.RowVersion);
        return Results.Ok(MapCourseV1(updated));
    }
    catch (ConcurrencyConflictException)
    {
        http.Items["errorCode"] = "concurrency.conflict";
        return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "ETag mismatch");
    }
});

v1.MapPost("/sections", (CreateSectionBody body, CampusStore store) =>
{
    if (!TryValidate(body, out var errors))
    {
        return ValidationProblem(errors);
    }

    try
    {
        var section = store.AddSection(body.CourseId, body.Term, body.Capacity);
        return Results.Created($"/api/v1/sections/{section.Id}", MapSection(section));
    }
    catch (KeyNotFoundException)
    {
        return NotFoundProblem();
    }
});

v1.MapPost("/enrollments", async (HttpContext http, CreateEnrollmentBody body, CampusStore store) =>
{
    if (!TryValidate(body, out var errors))
    {
        return ValidationProblem(errors);
    }

    http.Request.EnableBuffering();
    using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
    http.Request.Body.Position = 0;
    var raw = await reader.ReadToEndAsync();
    http.Request.Body.Position = 0;
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault();

    try
    {
        var enrollment = store.Enroll(body.StudentId, body.SectionId, key, hash);
        return Results.Created($"/api/v1/enrollments/{enrollment.Id}", MapEnrollmentV1(enrollment));
    }
    catch (KeyNotFoundException)
    {
        return NotFoundProblem();
    }
    catch (IdempotencyConflictException)
    {
        http.Items["errorCode"] = "idempotency.conflict";
        return Results.Conflict(new ProblemDetails
        {
            Title = "Idempotency-Key reused with different body",
            Status = StatusCodes.Status409Conflict,
            Extensions = { ["errorCode"] = "idempotency.conflict" },
        });
    }
    catch (InvalidOperationException ex) when (ex.Message == ErrorCodes.EnrollmentDuplicate)
    {
        http.Items["errorCode"] = ErrorCodes.EnrollmentDuplicate;
        return Results.Conflict(new { errorCode = ErrorCodes.EnrollmentDuplicate });
    }
});

// Deprecated v1 demo endpoint
v1.MapGet("/legacy/ping", (HttpContext http) =>
{
    http.Response.Headers["Deprecation"] = "true";
    http.Response.Headers["Sunset"] = "Wed, 01 Jul 2027 00:00:00 GMT";
    return Results.Ok(new { message = "use /api/v2/enrollments shape instead", deprecated = true });
});

// --- v2: enrollment response shape change (breaking demo) ---
v2.MapGet("/enrollments/{id:guid}", (Guid id, CampusStore store) =>
{
    var e = store.GetEnrollment(id);
    return e is null ? NotFoundProblem() : Results.Ok(MapEnrollmentV2(e));
});

v2.MapPost("/enrollments", (CreateEnrollmentBody body, CampusStore store, HttpContext http) =>
{
    if (!TryValidate(body, out var errors))
    {
        return ValidationProblem(errors);
    }

    var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body))));
    try
    {
        var enrollment = store.Enroll(body.StudentId, body.SectionId, key, hash);
        return Results.Created($"/api/v2/enrollments/{enrollment.Id}", MapEnrollmentV2(enrollment));
    }
    catch (KeyNotFoundException)
    {
        return NotFoundProblem();
    }
    catch (IdempotencyConflictException)
    {
        return Results.Conflict(new { errorCode = "idempotency.conflict" });
    }
    catch (InvalidOperationException ex) when (ex.Message == ErrorCodes.EnrollmentDuplicate)
    {
        return Results.Conflict(new { errorCode = ErrorCodes.EnrollmentDuplicate });
    }
});

app.Run();

static object MapCourseV1(CourseEntity c) => new
{
    id = c.Id,
    code = c.Code,
    title = c.Title,
    credits = c.Credits,
    createdAt = c.CreatedAt,
};

static object MapSection(SectionEntity s) => new
{
    id = s.Id,
    courseId = s.CourseId,
    term = s.Term,
    capacity = s.Capacity,
    seatsRemaining = s.SeatsRemaining,
};

static object MapEnrollmentV1(EnrollmentEntity e) => new
{
    id = e.Id,
    studentId = e.StudentId,
    sectionId = e.SectionId,
    status = e.Status.ToString(),
    createdAt = e.CreatedAt,
};

static object MapEnrollmentV2(EnrollmentEntity e) => new
{
    enrollmentId = e.Id,
    student = new { id = e.StudentId },
    section = new { id = e.SectionId },
    state = e.Status.ToString().ToLowerInvariant(),
    enrolledAt = e.CreatedAt,
};

static string ETag(long rowVersion) => $"\"{rowVersion}\"";

static bool TryReadIfMatch(HttpContext http, out long version, out IResult? error)
{
    version = 0;
    error = null;
    var raw = http.Request.Headers.IfMatch.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(raw))
    {
        http.Items["errorCode"] = "precondition.required";
        error = Results.Json(
            new ProblemDetails
            {
                Title = "If-Match required",
                Status = StatusCodes.Status428PreconditionRequired,
                Extensions = { ["errorCode"] = "precondition.required" },
            },
            statusCode: StatusCodes.Status428PreconditionRequired);
        return false;
    }

    var token = raw.Trim().Trim('"');
    if (!long.TryParse(token, out version))
    {
        http.Items["errorCode"] = "precondition.invalid";
        error = Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid If-Match");
        return false;
    }

    return true;
}

static IResult NotFoundProblem() =>
    Results.NotFound(new ProblemDetails
    {
        Title = "Not found",
        Status = 404,
        Extensions = { ["errorCode"] = ErrorCodes.NotFound },
    });

static IResult ValidationProblem(Dictionary<string, string[]> errors) =>
    Results.ValidationProblem(errors, extensions: new Dictionary<string, object?>
    {
        ["errorCode"] = ErrorCodes.ValidationFailed,
    });

static bool TryValidate<T>(T body, out Dictionary<string, string[]> errors)
{
    var ctx = new System.ComponentModel.DataAnnotations.ValidationContext(body!);
    var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
    var ok = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(body!, ctx, results, true);
    errors = results
        .GroupBy(r => r.MemberNames.FirstOrDefault() ?? "")
        .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "invalid").ToArray());
    return ok;
}

public partial class Program;

public sealed class CreateCourseBody
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(2)]
    public string Code { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(2)]
    public string Title { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Range(1, 10)]
    public int Credits { get; set; }
}

public sealed class UpdateCourseBody
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(2)]
    public string Title { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Range(1, 10)]
    public int Credits { get; set; }
}

public sealed class CreateSectionBody
{
    [System.ComponentModel.DataAnnotations.Required]
    public Guid CourseId { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    public string Term { get; set; } = "";

    [System.ComponentModel.DataAnnotations.Range(1, 500)]
    public int Capacity { get; set; }
}

public sealed class CreateEnrollmentBody
{
    [System.ComponentModel.DataAnnotations.Required]
    public Guid StudentId { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    public Guid SectionId { get; set; }
}
