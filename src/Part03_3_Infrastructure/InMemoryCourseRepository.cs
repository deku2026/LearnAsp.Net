using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Part03_3.Application;
using Part03_3.Domain;

namespace Part03_3.Infrastructure;

public sealed class InMemoryCourseRepository : ICourseRepository
{
    private readonly ConcurrentDictionary<Guid, Course> _db = new();

    public Task AddAsync(Course course, CancellationToken ct = default)
    {
        _db[course.Id] = course;
        return Task.CompletedTask;
    }

    public Task<Course?> GetAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_db.GetValueOrDefault(id));

    public Task<IReadOnlyList<Course>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Course>>(_db.Values.OrderBy(c => c.Code).ToList());
}

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPart03_3Infrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ICourseRepository, InMemoryCourseRepository>();
        return services;
    }
}
