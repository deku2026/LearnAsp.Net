using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Campus.ServiceDefaults;

public static class CampusTelemetry
{
    public const string ActivitySourceName = "Campus.Learning";
    public const string MeterName = "Campus.Learning";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "campus.operations",
        "{operation}",
        "Completed Campus lab operations.");

    public static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "campus.failures",
        "{failure}",
        "Failed Campus lab operations.");

    public static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "campus.operation.duration",
        "ms",
        "Campus lab operation duration.");
}
