using Part11_2_NativeAotTrim;
using Part11_2_NativeAotTrim.Endpoints;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = AotJsonContext.Default;
});

var app = builder.Build();

app.MapCourseEndpoints();

app.MapPost("/api/enrollments/validate", (ValidateEnrollmentRequest request) =>
{
    if (request.StudentId == Guid.Empty)
    {
        return Results.BadRequest(new ValidateEnrollmentResult(false, "studentId.required"));
    }
    var course = CourseCatalog.Find(request.CourseCode);
    return course is null
        ? Results.Ok(new ValidateEnrollmentResult(false, "course.not_found"))
        : Results.Ok(new ValidateEnrollmentResult(true, "ok"));
});

app.MapGet("/api/runtime-shape", () => new RuntimeShapeDto(
    PublishForm: "NativeAOT",
    Framework: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    ProcessArchitecture: System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    IsAotCompatible: true));

app.MapGet("/health/live", () => Results.Ok());
app.MapGet("/health/ready", () => Results.Ok());

app.Run();

public partial class Program;
