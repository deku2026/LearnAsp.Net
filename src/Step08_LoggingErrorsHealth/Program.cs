// LearnAspNet placeholder
// Doc   : ASP.NetStudy/步骤8-日志-错误处理-健康检查-完整实施指南.md
// Part  : Step08 · LoggingErrorsHealth
// Title : 日志·错误处理·健康检查

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Step08 · 日志·错误处理·健康检查 — placeholder, fill src/Step08_LoggingErrorsHealth/Program.cs");

app.Run();

public partial class Program;
