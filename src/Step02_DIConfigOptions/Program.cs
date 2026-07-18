// LearnAspNet
// Doc   : ASP.NetStudy/步骤2-依赖注入-配置-Options-完整实施指南.md
// Part  : Step02 · DIConfigOptions
// Title : 依赖注入 · 配置 · Options

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Step02_DIConfigOptions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<TransientMarker>();
builder.Services.AddScoped<ScopedMarker>();
builder.Services.AddSingleton<SingletonMarker>();

builder.Services
    .AddOptions<CampusLabOptions>()
    .Bind(builder.Configuration.GetSection(CampusLabOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<CampusLabOptions>, CampusLabOptionsValidator>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { lab = "Step02_DIConfigOptions" }));

app.MapGet("/di-demo", (TransientMarker t1, TransientMarker t2, ScopedMarker s1, ScopedMarker s2, SingletonMarker single) =>
{
    return Results.Ok(new
    {
        transient = new { first = t1.Id, second = t2.Id, same = t1.Id == t2.Id },
        scoped = new { first = s1.Id, second = s2.Id, same = s1.Id == s2.Id },
        singleton = single.Id,
    });
});

app.MapGet("/options", (IOptions<CampusLabOptions> opt, IOptionsSnapshot<CampusLabOptions> snap, IOptionsMonitor<CampusLabOptions> mon) =>
{
    return Results.Ok(new
    {
        options = opt.Value,
        snapshot = snap.Value,
        monitor = mon.CurrentValue,
    });
});

app.Run();

public partial class Program;

namespace Step02_DIConfigOptions
{
    public sealed class TransientMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed class ScopedMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed class SingletonMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public sealed class CampusLabOptions
    {
        public const string SectionName = "CampusLab";

        [Required]
        [MinLength(3)]
        public string LabName { get; set; } = "Step02";

        [Range(1, 100)]
        public int MaxSampleSize { get; set; } = 10;
    }

    public sealed class CampusLabOptionsValidator : IValidateOptions<CampusLabOptions>
    {
        public ValidateOptionsResult Validate(string? name, CampusLabOptions options)
        {
            if (string.Equals(options.LabName, "invalid", StringComparison.OrdinalIgnoreCase))
            {
                return ValidateOptionsResult.Fail("LabName cannot be 'invalid'.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
