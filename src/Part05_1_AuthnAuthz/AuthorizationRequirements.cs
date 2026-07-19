using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Part05_1_AuthnAuthz;

public sealed record ScopeRequirement(string Scope) : IAuthorizationRequirement;

public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        var scopes = context.User.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (scopes.Contains(requirement.Scope, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public interface IOwnedResource
{
    string OwnerSubject { get; }
}

public sealed record SameOwnerRequirement : IAuthorizationRequirement;

public sealed class SameOwnerAuthorizationHandler :
    AuthorizationHandler<SameOwnerRequirement, IOwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SameOwnerRequirement requirement,
        IOwnedResource resource)
    {
        var subject = context.User.FindFirstValue("sub") ??
                      context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (context.User.IsInRole("Admin") ||
            (!string.IsNullOrWhiteSpace(subject) &&
             string.Equals(subject, resource.OwnerSubject, StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
