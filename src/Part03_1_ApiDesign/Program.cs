// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第3部分-1-生产API设计-完整实施指南.md
// Part  : Part03-1 · ApiDesign
// Title : 生产 API 设计

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part03-1 · 生产 API 设计 — placeholder, fill src/Part03_1_ApiDesign/Program.cs");

app.Run();

public partial class Program;
