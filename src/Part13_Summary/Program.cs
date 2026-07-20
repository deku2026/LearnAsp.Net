using Campus.ServiceDefaults;
using Part13_Summary;

var builder = WebApplication.CreateBuilder(args);
builder.AddCampusServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ManifestLoader>();

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part13_Summary",
    purpose = "read-only capability index backed by versioned manifests",
    endpoints = new[]
    {
        "/api/capabilities",
        "/api/capstones",
        "/api/infrastructure",
        "/api/evidence",
        "/health/live",
        "/health/ready",
    },
}));

app.MapGet("/api/capabilities", (ManifestLoader m) =>
    m.Capabilities is null ? Results.NotFound() : Results.Ok(m.Capabilities));

app.MapGet("/api/capstones", (ManifestLoader m) =>
    m.Capstones is null ? Results.NotFound() : Results.Ok(m.Capstones));

app.MapGet("/api/infrastructure", (ManifestLoader m) =>
    m.Infrastructure is null ? Results.NotFound() : Results.Ok(m.Infrastructure));

app.MapGet("/api/evidence", (ManifestLoader m) =>
    m.Evidence is null ? Results.NotFound() : Results.Ok(m.Evidence));

app.MapCampusDefaultEndpoints();
app.Run();

public partial class Program;
