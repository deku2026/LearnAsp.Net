using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Part11_1_PerformanceAdvanced;

namespace Part11_1_PerformanceBenchmarks;

[MemoryDiagnoser]
public class CourseCodeParsingBenchmarks
{
    [Params("CS-1010-A-2026F", "PHYS-2200-B-2027S")]
    public string Code { get; set; } = "";

    [Benchmark(Baseline = true)]
    public CourseCodeParseResult? Baseline() => CourseCodeParser.ParseBaseline(Code);

    [Benchmark]
    public CourseCodeParseResult? Span() => CourseCodeParser.ParseSpan(Code);
}
