// LearnAspNet
// Doc   : ASP.NetStudy/步骤1-承载与启动模型-完整实施指南.md
// Part  : Step01 · HostStartup
// Title : 承载与启动模型

using Campus.Contracts;
using Step01_HostStartup;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<HeartbeatHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HeartbeatHostedService>());

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { lab = "Step01_HostStartup", status = "ok" }));

app.MapGet("/env", (IHostEnvironment env, IConfiguration config) =>
{
    var info = new EnvInfoDto(env.EnvironmentName, env.ContentRootPath, env.ApplicationName);
    return Results.Ok(info);
});

app.MapGet("/heartbeat-count", (HeartbeatHostedService hb) => Results.Ok(new { count = hb.TickCount }));

app.Run();

public partial class Program;
