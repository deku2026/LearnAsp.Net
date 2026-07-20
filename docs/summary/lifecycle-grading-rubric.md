# Lifecycle grading rubric (manual evidence)

This rubric is for the human "talk through a request's lifecycle without notes"
acceptance. It is NOT automated. Score each stage pass/fail with one key type
and one common pitfall named out loud. The candidate must pass all 10 to claim
the source-read lab complete.

| # | Stage | Key type(s) | Common pitfall | Pass/Fail |
|---|-------|-------------|----------------|-----------|
| 1 | Kestrel receives bytes | `KestrelServer`, `ConnectionHandler`, `Http1Connection` | Assuming Kestrel parses HTTP in the managed pipeline — it parses before `HttpContext` exists | |
| 2 | HostingApplication creates HttpContext + scope | `HostingApplication`, `DefaultHttpContext`, `IServiceScopeFactory` | Forgetting that the request scope is created here, not in the endpoint | |
| 3 | Middleware pipeline fold | `ApplicationBuilder.Build`, `RequestDelegate` | Registering `UseExceptionHandler` after `UseRouting` so routing exceptions escape | |
| 4 | Routing match + SetEndpoint | `EndpointRoutingMiddleware`, `DfaMatcher` | Calling `UseAuthorization` before `UseRouting` so there is no endpoint metadata | |
| 5 | Authentication sets User | `AuthenticationMiddleware`, `IAuthenticationSchemeProvider`, `ClaimsPrincipal` | Confusing Challenge (401, re-auth) with Forbid (403, deny) | |
| 6 | Authorization reads endpoint metadata | `AuthorizationMiddleware`, `AuthorizationPolicy`, `IAuthorizationHandler` | Putting auth logic in the endpoint instead of in a policy/requirement | |
| 7 | Endpoint / RDG executes | `EndpointMiddleware`, `RequestDelegateFactory` (RDG in AOT) | Referenced library not enabling RDG explicitly (W9 Part11_2 pitfall) | |
| 8 | DI + Options participate | `IServiceProvider`, `OptionsFactory`, `IOptionsMonitor` | Resolving a scoped service from the root (captive dependency) | |
| 9 | Response reverses through pipeline | `RequestDelegate` reverse fold | Writing to the response body after `next` returns when the response has already started | |
| 10 | Kestrel writes bytes back | `HttpResponse`, `KestrelServer` response flush | Calling `StartAsync` twice or writing headers after the body has started | |

## Instructions

1. Sit with a whiteboard or text editor, no IDE, no browser, no notes.
2. Walk through a single request to `/api/courses/CS-1010` on the W9 Part11_2
   AOT app from TCP accept to TCP close.
3. For each stage, name the key type and one pitfall. If you cannot, mark it
   fail and revisit the corresponding W1-W9 lab and the source map in
   `docs/framework-source/01-di-middleware-routing-options-auth.md`.
4. All 10 must pass. This is manual evidence — do not fake it as an automated
   test result. Record the date, the version (SDK, runtime, commit), and the
   pass/fail per stage in your learning notes.
