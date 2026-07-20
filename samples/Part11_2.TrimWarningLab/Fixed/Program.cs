// Fixed variant: eliminate reflection entirely (fix order step 1 - preferred).
// The other three steps (DynamicallyAccessedMembers, RequiresUnreferencedCode,
// UnconditionalSuppressMessage) are documented in the script output and the
// article docs/performance/w9-aot-lab.md.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/break", () => Guid.NewGuid());
app.MapGet("/health/live", () => Results.Ok());
app.Run();

public partial class Program;