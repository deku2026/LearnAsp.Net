// LearnAspNet placeholder
// Doc   : ASP.NetStudy/步骤4-路由与终结点-完整实施指南.md
// Part  : Step04 · RoutingEndpoints
// Title : 路由与终结点

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Step04 · 路由与终结点 — placeholder, fill src/Step04_RoutingEndpoints/Program.cs");

app.Run();

public partial class Program;
