// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第3部分-2-应用架构-完整实施指南.md
// Part  : Part03-2 · AppArchitecture
// Title : 应用架构

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part03-2 · 应用架构 — placeholder, fill src/Part03_2_AppArchitecture/Program.cs");

app.Run();

public partial class Program;
