// LearnAspNet placeholder
// Doc   : ASP.NetStudy/步骤1-承载与启动模型-完整实施指南.md
// Part  : Step01 · HostStartup
// Title : 承载与启动模型

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Step01 · 承载与启动模型 — placeholder, fill src/Step01_HostStartup/Program.cs");

app.Run();

public partial class Program;
