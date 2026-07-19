using Grpc.Core;
using Part07_DistributedComm.Grpc;

namespace Part07_DistributedComm;

public sealed class CatalogGrpcService(
    CatalogStore store,
    TimeProvider timeProvider) : Catalog.CatalogBase
{
    public override async Task<CourseReply> GetCourse(
        GetCourseRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.CourseId, out var courseId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "course_id must be a UUID."));
        }

        var course = await store.GetAsync(courseId, context.CancellationToken);
        if (course is null)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Course {courseId} does not exist."));
        }

        return ToReply(course);
    }

    public override async Task WatchAvailability(
        WatchAvailabilityRequest request,
        IServerStreamWriter<AvailabilityReply> responseStream,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.CourseId, out var courseId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "course_id must be a UUID."));
        }

        var samples = Math.Clamp(request.Samples, 1, 5);
        for (var index = 0; index < samples; index++)
        {
            var course = await store.GetAsync(courseId, context.CancellationToken);
            if (course is null)
            {
                throw new RpcException(new Status(
                    StatusCode.NotFound,
                    $"Course {courseId} does not exist."));
            }

            await responseStream.WriteAsync(new AvailabilityReply
            {
                CourseId = course.Id.ToString(),
                AvailableSeats = course.AvailableSeats,
                ObservedOnUnixMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
            });
            await Task.Delay(25, context.CancellationToken);
        }
    }

    private static CourseReply ToReply(CatalogCourse course) => new()
    {
        CourseId = course.Id.ToString(),
        Code = course.Code,
        Title = course.Title,
        AvailableSeats = course.AvailableSeats,
    };
}
