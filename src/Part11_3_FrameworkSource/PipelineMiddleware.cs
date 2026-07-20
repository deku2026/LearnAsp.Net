namespace Part11_3_FrameworkSource;

public sealed class PipelineMiddleware(RequestDelegate next, string label)
{
    public async Task InvokeAsync(HttpContext context, ILogger<PipelineMiddleware> logger)
    {
        context.Items[$"before:{label}"] = DateTimeOffset.UtcNow.Ticks;
        logger.LogInformation("Pipeline before {Label}", label);
        await next(context);
        logger.LogInformation("Pipeline after {Label}", label);
        context.Items[$"after:{label}"] = DateTimeOffset.UtcNow.Ticks;
    }
}
