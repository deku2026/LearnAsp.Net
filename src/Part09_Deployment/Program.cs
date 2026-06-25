// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第9部分-部署-完整实施指南.md
// Part  : Part09 · Deployment
// Title : 部署 (Docker · Compose · GH Actions · ACA · K8s · AKS · KEDA)

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part09 · 部署 — placeholder, fill src/Part09_Deployment/Program.cs");

app.Run();

public partial class Program;
