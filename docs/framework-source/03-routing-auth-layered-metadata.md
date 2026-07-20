# Why routing, authentication, and authorization are layered: endpoint metadata lifecycle

## The question

Why does ASP.NET Core separate routing, authentication, and authorization into
three middleware instead of one? Why does the authorization handler read from
endpoint metadata rather than from the route template or the controller
attribute directly?

## Public entry point

`app.UseRouting()` adds `EndpointRoutingMiddleware`; `app.UseAuthentication()`
adds `AuthenticationMiddleware`; `app.UseAuthorization()` adds
`AuthorizationMiddleware`. The order matters because each reads from the
previous step's output.

## Key types

- `EndpointRoutingMiddleware` (`dotnet/aspnetcore`)
- `DfaMatcher` (`dotnet/aspnetcore`)
- `Endpoint` / `EndpointMetadataCollection` (`dotnet/aspnetcore`)
- `AuthenticationMiddleware` (`dotnet/aspnetcore`)
- `AuthorizationMiddleware` (`dotnet/aspnetcore`)
- `AuthorizationPolicy` / `IAuthorizationRequirement` / `IAuthorizationHandler`

## Happy-path call chain

1. `EndpointRoutingMiddleware` runs the DFA matcher against the path and
   method. The matcher returns an `Endpoint` (or null). The middleware calls
   `HttpContext.SetEndpoint(endpoint)`.
2. `AuthenticationMiddleware` gets the default scheme from
   `IAuthenticationSchemeProvider`, runs the handler, and sets
   `HttpContext.User` to a `ClaimsPrincipal`. This step is independent of the
   endpoint — it runs even if no endpoint matched.
3. `AuthorizationMiddleware` reads the `Endpoint` from `HttpContext.GetEndpoint()`,
   extracts `IAuthorizeData` and policy metadata from `EndpointMetadataCollection`,
   builds an `AuthorizationPolicy`, and evaluates it via the registered
   `IAuthorizationHandler` implementations. If it fails, it returns Challenge
   (401) or Forbid (403). If it succeeds, it calls `next`.
4. The terminal `EndpointMiddleware` executes the endpoint delegate.

The layering exists because each concern has a different input and output:

- Routing produces an endpoint (metadata, delegate) from the raw path.
- Authentication produces a principal from the request (token, cookie, header).
- Authorization produces a decision from the principal + endpoint metadata.

Coupling them would mean the matcher has to know about auth schemes, or the
auth handler has to know about route templates. Keeping them separate lets you
swap any one without touching the others (e.g. JWT vs Keycloak OIDC in W6
without changing routing or authorization).

## Minimal experiment

Run the W9 `Part11_3_FrameworkSource` lab:

```
curl -H "X-Lab-Token: <token>" http://localhost:5029/lab/endpoint-metadata
```

The `/lab/endpoint-metadata` endpoint is registered with
`.WithMetadata(new LabPolicy("demo"))`. The `LabPolicyHandler` reads
`http.GetEndpoint()?.Metadata.GetMetadata<LabPolicy>()` and succeeds only if
the policy is present. This proves that custom metadata placed at endpoint
registration time is available to the authorization handler — the handler does
not need to inspect the route template or the delegate.

## Callout to a real W1-W9 problem

The W1 `Step07_AuthnAuthzEntry` lab uses `FallbackPolicy` to enforce
authentication on all endpoints that do not explicitly allow anonymous. The
policy is read from the endpoint metadata (`AllowAnonymousAttribute` is also
metadata). If `UseAuthorization` ran before `UseRouting`, there would be no
endpoint, so the policy would have no metadata to read and the fallback would
either fire on every request (breaking anonymous endpoints) or never fire
(breaking the protected ones).

The W6 `Part05_1_AuthnAuthz` lab extends this to resource authorization: the
handler reads the `college_id` claim from the principal (set by
authentication) and the tenant ID from the route (set by routing) to decide
whether the student can access a specific section. That only works because
routing set the endpoint, authentication set the user, and authorization
reads both.

## Pinned source links

- `EndpointRoutingMiddleware`:
  <https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Http/Routing/src/EndpointRoutingMiddleware.cs>
- `AuthorizationMiddleware`:
  <https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs>
- `DfaMatcher`:
  <https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Http/Routing/src/Matching/DfaMatcher.cs>

Verify the tag matches the running assemblies via Source Link.