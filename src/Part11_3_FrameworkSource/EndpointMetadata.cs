using Microsoft.AspNetCore.Authorization;

namespace Part11_3_FrameworkSource;

public sealed record LabPolicy(string Name);

public sealed class LabPolicyHandler : AuthorizationHandler<LabPolicyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, LabPolicyRequirement requirement)
    {
        if (context.Resource is HttpContext http)
        {
            var endpoint = http.GetEndpoint();
            var policy = endpoint?.Metadata.GetMetadata<LabPolicy>();
            if (policy is not null)
            {
                context.Succeed(requirement);
            }
        }
        return Task.CompletedTask;
    }
}

public sealed class LabPolicyRequirement : IAuthorizationRequirement;