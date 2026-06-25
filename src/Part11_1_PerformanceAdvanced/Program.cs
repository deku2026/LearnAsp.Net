// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第11部分-1-性能进阶-完整实施指南.md
// Part  : Part11-1 · PerformanceAdvanced
// Title : 性能进阶 (ThreadPool · ValueTask · ArrayPool · Span · 源生成 · 响应压缩 · Kestrel 调优)

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part11-1 · 性能进阶 — placeholder, fill src/Part11_1_PerformanceAdvanced/Program.cs");

app.Run();

public partial class Program;
