// LearnAspNet
// Doc   : ASP.NetStudy/步骤2-依赖注入-配置-Options-完整实施指南.md
// Part  : Step02 · DIConfigOptions
// Title : 依赖注入 · 配置 · Options

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Step02_DIConfigOptions;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// Three lifetimes
builder.Services.AddTransient<TransientMarker>();
builder.Services.AddScoped<ScopedMarker>();
builder.Services.AddSingleton<SingletonMarker>();

// Options + ValidateOnStart + custom IValidateOptions
builder.Services
    .AddOptions<CampusLabOptions>()
    .Bind(builder.Configuration.GetSection(CampusLabOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<CampusLabOptions>, CampusLabOptionsValidator>();

// IOptionsMonitor.OnChange watcher (singleton demonstrating hot-reload)
builder.Services.AddSingleton<OptionsWatcher>();

// Captive dependency: commented out — would throw with ValidateScopes=true.
// Uncomment to reproduce: builder.Services.AddSingleton<CaptiveSingleton>();
// Safe alternative uses IServiceScopeFactory inside the singleton.
builder.Services.AddSingleton<ScopedResolverSingleton>();

// Scrutor: decorator pattern
builder.Services.AddSingleton<ICounter, Counter>();
builder.Services.Decorate<ICounter, CachingCounter>();

// Scrutor: scan registration for INotifier implementations
builder.Services.Scan(scan => scan
    .FromAssemblyOf<Program>()
    .AddClasses(classes => classes.AssignableTo<INotifier>())
    .AsImplementedInterfaces()
    .WithSingletonLifetime());

// Keyed services
builder.Services.AddKeyedSingleton<IChannel, EmailChannel>("email");
builder.Services.AddKeyedSingleton<IChannel, SmsChannel>("sms");

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

app.MapGet("/options/watcher", (OptionsWatcher watcher) => Results.Ok(new { watcher.LastSeenLabName, watcher.LastSeenMaxSampleSize }));

app.MapGet("/captive-demo", (ScopedResolverSingleton svc) => Results.Ok(new
{
    resolvedIds = svc.ResolveInIndependentScopes(2),
}));

app.MapGet("/counter", (ICounter counter) => Results.Ok(new { value = counter.Count(), decorated = counter is CachingCounter }));

app.MapGet("/notifiers", (IEnumerable<INotifier> notifiers) =>
    Results.Ok(new { count = notifiers.Count(), types = notifiers.Select(n => n.GetType().Name).ToArray() }));

app.MapGet("/channels/email", ([FromKeyedServices("email")] IChannel channel) =>
    Results.Ok(new { key = "email", type = channel.GetType().Name, message = channel.Send() }));
app.MapGet("/channels/sms", ([FromKeyedServices("sms")] IChannel channel) =>
    Results.Ok(new { key = "sms", type = channel.GetType().Name, message = channel.Send() }));

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

    // IOptionsMonitor.OnChange: singleton watches hot-reloads
    public sealed class OptionsWatcher : IDisposable
    {
        private readonly object _stateLock = new();
        private readonly IDisposable? _subscription;
        private string _lastSeenLabName;
        private int _lastSeenMaxSampleSize;
        private int _changeCount;

        public OptionsWatcher(IOptionsMonitor<CampusLabOptions> monitor)
        {
            _lastSeenLabName = monitor.CurrentValue.LabName;
            _lastSeenMaxSampleSize = monitor.CurrentValue.MaxSampleSize;
            _subscription = monitor.OnChange(o =>
            {
                lock (_stateLock)
                {
                    _lastSeenLabName = o.LabName;
                    _lastSeenMaxSampleSize = o.MaxSampleSize;
                    _changeCount++;
                }
            });
        }

        public string LastSeenLabName
        {
            get
            {
                lock (_stateLock)
                {
                    return _lastSeenLabName;
                }
            }
        }

        public int LastSeenMaxSampleSize
        {
            get
            {
                lock (_stateLock)
                {
                    return _lastSeenMaxSampleSize;
                }
            }
        }

        public int ChangeCount
        {
            get
            {
                lock (_stateLock)
                {
                    return _changeCount;
                }
            }
        }

        public void Dispose() => _subscription?.Dispose();
    }

    // Captive dependency: a singleton that safely resolves scoped via IServiceScopeFactory.
    // The UNSAFE version (injecting ScopedMarker directly) would throw with ValidateScopes=true.
    public sealed class ScopedResolverSingleton(IServiceScopeFactory scopeFactory)
    {
        public IReadOnlyList<Guid> ResolveInIndependentScopes(int count)
        {
            var resolvedIds = new List<Guid>(count);
            for (var i = 0; i < count; i++)
            {
                using var scope = scopeFactory.CreateScope();
                var marker = scope.ServiceProvider.GetRequiredService<ScopedMarker>();
                resolvedIds.Add(marker.Id);
            }

            return resolvedIds;
        }
    }

    public sealed class CaptiveSingleton(ScopedMarker scopedMarker)
    {
        public Guid CapturedId { get; } = scopedMarker.Id;
    }

    // Scrutor decorator: ICounter → CachingCounter wraps Counter
    public interface ICounter
    {
        int Count();
    }

    public sealed class Counter : ICounter
    {
        private int _n;
        public int Count() => Interlocked.Increment(ref _n);
    }

    public sealed class CachingCounter(ICounter inner) : ICounter
    {
        private int _last;
        public int Count()
        {
            var value = inner.Count();
            Interlocked.Exchange(ref _last, value);
            return value;
        }

        public int LastValue => Volatile.Read(ref _last);
    }

    // INotifier multi-impl resolved via IEnumerable<INotifier>
    public interface INotifier
    {
        string Notify();
    }

    public sealed class EmailNotifier : INotifier
    {
        public string Notify() => "email sent";
    }

    public sealed class SmsNotifier : INotifier
    {
        public string Notify() => "sms sent";
    }

    // Keyed services
    public interface IChannel
    {
        string Send();
    }

    public sealed class EmailChannel : IChannel
    {
        public string Send() => "email channel";
    }

    public sealed class SmsChannel : IChannel
    {
        public string Send() => "sms channel";
    }
}
