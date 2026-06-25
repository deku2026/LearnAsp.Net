// LearnAspNet placeholder
// Doc   : ASP.NetStudy/步骤3-中间件管道-完整实施指南.md
// Part  : Step03 · MiddlewarePipeline
// Title : 中间件管道

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Step03 · 中间件管道 — placeholder, fill src/Step03_MiddlewarePipeline/Program.cs");

app.Run();

public partial class Program;
