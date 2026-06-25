// LearnAspNet placeholder
// Doc   : ASP.NetStudy/步骤10-HTTP底座-Kestrel-HttpClientFactory-完整实施指南.md
// Part  : Step10 · HttpFoundation
// Title : HTTP 底座 (Kestrel · HttpClientFactory)

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Step10 · HTTP 底座 — placeholder, fill src/Step10_HttpFoundation/Program.cs");

app.Run();

public partial class Program;
