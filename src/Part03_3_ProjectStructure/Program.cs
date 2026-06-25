// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第3部分-3-项目结构-完整实施指南.md
// Part  : Part03-3 · ProjectStructure
// Title : 项目结构

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part03-3 · 项目结构 — placeholder, fill src/Part03_3_ProjectStructure/Program.cs");

app.Run();

public partial class Program;
