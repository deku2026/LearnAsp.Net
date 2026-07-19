namespace Part05_2_SpaAuth;

public static class BffCsrfProtection
{
    public const string HeaderName = "X-CSRF";
    public const string HeaderValue = "1";

    public static string? Validate(HttpRequest request, BffOptions options)
    {
        if (!string.Equals(
                request.Headers[HeaderName].FirstOrDefault(),
                HeaderValue,
                StringComparison.Ordinal))
        {
            return "csrf_header_missing";
        }

        var origin = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(origin))
        {
            return null;
        }

        var expectedOrigin = options.PublicOrigin;
        if (string.IsNullOrWhiteSpace(expectedOrigin))
        {
            expectedOrigin = $"{request.Scheme}://{request.Host}";
        }

        return string.Equals(
            origin.TrimEnd('/'),
            expectedOrigin.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase)
            ? null
            : "cross_origin_request_rejected";
    }
}

public sealed class BffCsrfMiddleware(
    RequestDelegate next,
    Microsoft.Extensions.Options.IOptions<BffOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            // Deliberately omit Access-Control-Allow-* headers. A cross-origin browser
            // preflight therefore fails and never reaches the downstream API.
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        var error = BffCsrfProtection.Validate(context.Request, options.Value);
        if (error is not null)
        {
            var status = error == "cross_origin_request_rejected"
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status400BadRequest;
            await BffProblemDetails.WriteAsync(
                context.Response,
                status,
                "The BFF request failed CSRF validation.",
                error,
                context.RequestAborted);
            return;
        }

        await next(context);
    }
}
