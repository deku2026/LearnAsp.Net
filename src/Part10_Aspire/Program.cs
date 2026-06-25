// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第10部分-Aspire-完整实施指南.md
// Part  : Part10 · Aspire
// Title : Aspire (AppHost · ServiceDefaults · Dashboard · Integrations)
// Note  : Web exe placeholder now; promote csproj to Aspire.Hosting.Sdk when this section is filled in.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part10 · Aspire — placeholder, fill src/Part10_Aspire/Program.cs (and promote csproj to Aspire.Hosting.Sdk)");

app.Run();

public partial class Program;
