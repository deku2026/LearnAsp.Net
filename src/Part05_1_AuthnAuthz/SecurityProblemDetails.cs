using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Part05_1_AuthnAuthz;

public static class SecurityProblemDetails
{
    public static async Task WriteAsync(
        HttpResponse response,
        int status,
        string title,
        string errorCode,
        CancellationToken cancellationToken = default)
    {
        response.StatusCode = status;
        response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.com/{status}",
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = response.HttpContext.TraceIdentifier;
        await JsonSerializer.SerializeAsync(
            response.Body,
            problem,
            cancellationToken: cancellationToken);
    }
}
