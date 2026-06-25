// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第11部分-3-框架源码精读-完整实施指南.md
// Part  : Part11-3 · FrameworkSource
// Title : 框架源码精读 (DI · Pipeline · Routing · Options · Auth)

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part11-3 · 框架源码精读 — placeholder, fill src/Part11_3_FrameworkSource/Program.cs");

app.Run();

public partial class Program;
