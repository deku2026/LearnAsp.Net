using System.Text.Json.Serialization;
using Part11_2_NativeAotTrim.Endpoints;

namespace Part11_2_NativeAotTrim;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RuntimeShapeDto))]
[JsonSerializable(typeof(ValidateEnrollmentRequest))]
[JsonSerializable(typeof(ValidateEnrollmentResult))]
[JsonSerializable(typeof(CourseDto))]
[JsonSerializable(typeof(CourseNotFoundResponse))]
public partial class AotJsonContext : JsonSerializerContext;
