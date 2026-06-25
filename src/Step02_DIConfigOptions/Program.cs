// LearnAspNet placeholder
// Doc   : ASP.NetStudy/步骤2-依赖注入-配置-Options-完整实施指南.md
// Part  : Step02 · DIConfigOptions
// Title : 依赖注入·配置·Options

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Step02 · 依赖注入·配置·Options — placeholder, fill src/Step02_DIConfigOptions/Program.cs");

app.Run();

public partial class Program;
