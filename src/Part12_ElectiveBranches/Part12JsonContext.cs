using System.Text.Json.Serialization;

namespace Part12_ElectiveBranches;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EnrollmentActivityEvent))]
[JsonSerializable(typeof(ScheduleEmailRequest))]
[JsonSerializable(typeof(EmailJobStatus))]
[JsonSerializable(typeof(List<EmailJobStatus>))]
[JsonSerializable(typeof(KafkaProduceResult))]
[JsonSerializable(typeof(KafkaStatusDto))]
public partial class Part12JsonContext : JsonSerializerContext;
