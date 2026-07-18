// LearnAspNet
// Doc   : ASP.NetStudy/第3部分-4-架构测试与契约兼容-完整实施指南.md
// Part  : Part03_4 · ArchTesting
// Title : 架构测试与契约兼容

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    lab = "Part03_4_ArchTesting",
    gates = new[]
    {
        "NetArchTest: Part03_3 layer laws",
        "Module boundary: Enrollment↛Catalog impl; Notices↛Enrollment impl",
        "Reflection: Course private ctor",
        "OpenAPI path smoke remains in Part03_1.Tests (contract surface)",
    },
    note = "oasdiff binary optional in CI later; path presence asserted in Part03_1 tests",
}));

app.MapGet("/arch/summary", () => Results.Ok(new
{
    layerLaws = new[]
    {
        "Domain ↛ Application/Infrastructure/EF/ASP.NET",
        "Application ↛ Infrastructure",
        "Contracts ↛ Domain",
        "Infrastructure → Application + Domain",
    },
    moduleLaws = new[]
    {
        "Enrollment → Catalog.Contracts only",
        "Notices → Enrollment.Contracts only",
        "Catalog ↛ Enrollment",
    },
}));

app.Run();

public partial class Program;
