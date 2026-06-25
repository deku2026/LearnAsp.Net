// LearnAspNet placeholder
// Doc   : ASP.NetStudy/步骤9-集成测试-完整实施指南.md
// Part  : Step09 · IntegrationTesting
// Title : 集成测试

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Step09 · 集成测试 — placeholder, fill src/Step09_IntegrationTesting/Program.cs");

app.Run();

public partial class Program;
