using Campus.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddCampusServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<DeploymentReadiness>();

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/", (
    IWebHostEnvironment environment,
    IConfiguration configuration) => Results.Ok(new
    {
        lab = "Part09_Deployment",
        environment = environment.EnvironmentName,
        revision = configuration["Deployment:Revision"] ?? "local",
        strategy = configuration["Deployment:Strategy"] ?? "rolling",
        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        processArchitecture =
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    }));

app.MapGet("/api/deployment/configuration", (
    IConfiguration configuration) => Results.Ok(new
    {
        revision = configuration["Deployment:Revision"] ?? "local",
        region = configuration["Deployment:Region"] ?? "local",
        strategy = configuration["Deployment:Strategy"] ?? "rolling",
        secrets = "Credentials are supplied by the deployment platform and are never returned.",
    }));

app.MapHealthChecks("/health/live", new()
{
    Predicate = check => check.Tags.Contains("live"),
});
app.MapGet("/health/ready", (DeploymentReadiness readiness) =>
    readiness.IsReady
        ? Results.Ok(new { status = "Healthy" })
        : Results.Json(
            new { status = "Unhealthy" },
            statusCode: StatusCodes.Status503ServiceUnavailable));

app.Lifetime.ApplicationStarted.Register(() =>
    app.Services.GetRequiredService<DeploymentReadiness>().MarkReady());

app.Run();

public partial class Program;

public sealed class DeploymentReadiness
{
    private int _ready;

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    public void MarkReady() => Interlocked.Exchange(ref _ready, 1);
}
