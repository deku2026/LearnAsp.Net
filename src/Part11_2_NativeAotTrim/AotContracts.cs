namespace Part11_2_NativeAotTrim;

public sealed record RuntimeShapeDto(
    string PublishForm,
    string Framework,
    string ProcessArchitecture,
    bool IsAotCompatible);

public sealed record ValidateEnrollmentRequest(Guid StudentId, string CourseCode);

public sealed record ValidateEnrollmentResult(bool Valid, string Reason);