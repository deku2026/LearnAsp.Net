// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第6部分-1-消息模式-完整实施指南.md
// Part  : Part06-1 · MessagingPatterns
// Title : 消息模式 (Outbox · Inbox · Saga · 幂等 · DLQ)

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part06-1 · 消息模式 — placeholder, fill src/Part06_1_MessagingPatterns/Program.cs");

app.Run();

public partial class Program;
