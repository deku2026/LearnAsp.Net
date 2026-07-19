using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Part05_1_AuthnAuthz;

public sealed class KeycloakRoleClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity ||
            !identity.IsAuthenticated ||
            identity.HasClaim(claim => claim.Type == "campus_roles_mapped"))
        {
            return Task.FromResult(principal);
        }

        AddRealmRoles(identity);
        AddClientRoles(identity);
        identity.AddClaim(new Claim("campus_roles_mapped", "true"));
        return Task.FromResult(principal);
    }

    private static void AddRealmRoles(ClaimsIdentity identity)
    {
        var realmAccess = identity.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess))
        {
            return;
        }

        using var document = JsonDocument.Parse(realmAccess);
        if (!document.RootElement.TryGetProperty("roles", out var roles))
        {
            return;
        }

        AddRoles(identity, roles);
    }

    private static void AddClientRoles(ClaimsIdentity identity)
    {
        var resourceAccess = identity.FindFirst("resource_access")?.Value;
        if (string.IsNullOrWhiteSpace(resourceAccess))
        {
            return;
        }

        using var document = JsonDocument.Parse(resourceAccess);
        foreach (var roles in document.RootElement
                     .EnumerateObject()
                     .Where(client => client.Value.TryGetProperty("roles", out _))
                     .Select(client => client.Value.GetProperty("roles")))
        {
            AddRoles(identity, roles);
        }
    }

    private static void AddRoles(ClaimsIdentity identity, JsonElement roles)
    {
        foreach (var value in roles
                     .EnumerateArray()
                     .Select(role => role.GetString())
                     .Where(value =>
                         !string.IsNullOrWhiteSpace(value) &&
                         !identity.HasClaim(ClaimTypes.Role, value)))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, value!));
        }
    }
}
