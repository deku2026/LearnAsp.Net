// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第11部分-2-NativeAOT与Trim-完整实施指南.md
// Part  : Part11-2 · NativeAotTrim
// Title : NativeAOT 与 Trim (无 JIT · 单文件 · ReadyToRun)

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part11-2 · NativeAOT 与 Trim — placeholder, fill src/Part11_2_NativeAotTrim/Program.cs");

app.Run();

public partial class Program;
