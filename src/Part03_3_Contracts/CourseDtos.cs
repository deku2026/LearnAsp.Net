namespace Part03_3.Contracts;

public sealed record CreateCourseRequest(string Code, string Title, int Credits);
public sealed record CourseResponse(Guid Id, string Code, string Title, int Credits);
