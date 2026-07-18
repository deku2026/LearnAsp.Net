// LearnAspNet
// Doc   : ASP.NetStudy/第3部分-4-架构测试与契约兼容-完整实施指南.md
// Part  : Part03_4 · ArchTesting
// Title : 架构测试与契约兼容

// This host is a thin pointer lab; architecture rules live in tests/Part03_4_ArchTesting.Tests
// and target Part03_3_* assemblies (layers + reference graph).

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part03_4_ArchTesting",
    targets = new[] { "Part03_3.Domain", "Part03_3.Application", "Part03_3.Infrastructure", "Part03_3_ProjectStructure" },
    gates = new[] { "NetArchTest layering", "handler naming", "OpenAPI contract smoke via Part03_1" },
}));

app.MapGet("/arch/summary", () => Results.Ok(new
{
    layerLaws = new[]
    {
        "Domain ↛ Application/Infrastructure/EF/ASP.NET",
        "Application ↛ Infrastructure",
        "Infrastructure → Application + Domain",
        "Api composition root wires all",
    },
    contract = "Prefer additive OpenAPI/event changes; breaking requires version coexistence",
}));

app.Run();

public partial class Program;
