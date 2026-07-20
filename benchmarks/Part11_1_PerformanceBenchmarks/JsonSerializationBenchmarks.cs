using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Part11_1_PerformanceAdvanced;
using System.Text.Json;

namespace Part11_1_PerformanceBenchmarks;

[MemoryDiagnoser]
public class JsonSerializationBenchmarks
{
    private EnrollmentSummaryDto _dto = null!;
    private JsonSerializerOptions _reflectionOptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dto = new EnrollmentSummaryDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Confirmed", DateTimeOffset.UtcNow);
        _reflectionOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }

    [Benchmark(Baseline = true)]
    public byte[] Reflection() =>
        JsonSerializer.SerializeToUtf8Bytes(_dto, _reflectionOptions);

    [Benchmark]
    public byte[] SourceGenerated() =>
        JsonSerializer.SerializeToUtf8Bytes(_dto, PerformanceJsonContext.Default.EnrollmentSummaryDto);
}