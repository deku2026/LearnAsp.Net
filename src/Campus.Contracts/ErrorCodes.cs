namespace Campus.Contracts;

public static class ErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string NotFound = "resource.not_found";
    public const string EnrollmentSectionFull = "enrollment.section_full";
    public const string EnrollmentDuplicate = "enrollment.duplicate";
    public const string EnrollmentNotFound = "enrollment.not_found";
    public const string AuthUnauthorized = "auth.unauthorized";
    public const string AuthForbidden = "auth.forbidden";
    public const string InternalError = "internal.error";
}
