// LearnAspNet placeholder
// Doc   : ASP.NetStudy/第4部分-2-缓存三层-完整实施指南.md
// Part  : Part04-2 · Caching
// Title : 缓存三层 (IMemoryCache · IDistributedCache · HybridCache · OutputCaching)

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "LearnAspNet · Part04-2 · 缓存三层 — placeholder, fill src/Part04_2_Caching/Program.cs");

app.Run();

public partial class Program;
