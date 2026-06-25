// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第3部分-4-架构测试与契约兼容-完整实施指南.md
// Part  : Part03-4 · ArchTesting
// Title : 架构测试与契约兼容

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part03-4 · 架构测试与契约兼容 — placeholder, fill src/Part03_4_ArchTesting/Program.cs");

app.Run();

public partial class Program;
