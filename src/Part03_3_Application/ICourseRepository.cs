using Part03_3.Domain;

namespace Part03_3.Application;

public interface ICourseRepository
{
    Task AddAsync(Course course, CancellationToken ct = default);
    Task<Course?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Course>> ListAsync(CancellationToken ct = default);
}

public interface ICreateCourseHandler
{
    Task<Course> HandleAsync(string code, string title, int credits, CancellationToken ct = default);
}

internal sealed class CreateCourseHandler(ICourseRepository repo) : ICreateCourseHandler
{
    public async Task<Course> HandleAsync(string code, string title, int credits, CancellationToken ct = default)
    {
        var course = new Course(code, title, credits);
        await repo.AddAsync(course, ct);
        return course;
    }
}

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddPart03_3Application(this IServiceCollection services)
    {
        services.AddScoped<ICreateCourseHandler, CreateCourseHandler>();
        return services;
    }
}
