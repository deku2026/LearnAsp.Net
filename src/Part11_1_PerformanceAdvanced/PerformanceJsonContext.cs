using System.Text.Json.Serialization;

namespace Part11_1_PerformanceAdvanced;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RuntimeInfoDto))]
[JsonSerializable(typeof(CourseCodeParseResult))]
[JsonSerializable(typeof(EnrollmentSummaryDto))]
[JsonSerializable(typeof(SerializeResultDto))]
[JsonSerializable(typeof(PayloadDto))]
[JsonSerializable(typeof(ParseRequest))]
[JsonSerializable(typeof(SerializeRequest))]
[JsonSerializable(typeof(List<EnrollmentSummaryDto>))]
public partial class PerformanceJsonContext : JsonSerializerContext;
