// INTENTIONAL: this sample is NOT in the solution and is expected to emit
// IL2026/IL3050 on publish. The script scripts/aot/publish-trim-warning-lab.sh
// asserts those warnings appear, then shows the fix order.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/break", () =>
{
    var t = Type.GetType("System.Guid, System.Private.CoreLib")!;
    var method = t.GetMethod("NewGuid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
    return method.Invoke(null, null);
});

app.MapGet("/health/live", () => Results.Ok());
app.Run();

public partial class Program;