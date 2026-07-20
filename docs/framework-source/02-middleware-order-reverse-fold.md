# Why ASP.NET Core middleware is ordered: from reverse fold to two-way tunnel

## The question

Why does adding middleware in a different order break behavior, and why does
`app.UseAuthentication()` have to come before `app.UseAuthorization()`? The
answer is in how `ApplicationBuilder.Build` folds the component list and how
the resulting chain carries the request forward and the response back.

## Public entry point

`WebApplication.Build()` calls `ApplicationBuilder.Build()`, which walks the
`_components` list in reverse, composing each `RequestDelegate` around the next.

## Key types

- `IApplicationBuilder` / `ApplicationBuilder` (`dotnet/aspnetcore`)
- `RequestDelegate` (`Func<HttpContext, Task>`)
- `UseMiddlewareExtensions` (`UseMiddleware<T>`)
- `IMiddleware` (factory-style middleware with DI-injected constructor)

## Happy-path call chain

1. `app.Use(...)` appends a `Func<RequestDelegate, RequestDelegate>` to
   `_components`.
2. `app.Build()` iterates `_components` in reverse, starting from a terminal
   `RequestDelegate` that returns 404.
3. Each component receives the "next" delegate and returns a new delegate that
   runs its own logic, then `await next(context)`, then post-next logic.
4. The returned delegate is the first component. Kestrel invokes it.
5. The request flows forward (component 1 before -> component 2 before -> ...
   -> terminal). The response flows back (terminal -> ... -> component 2
   after -> component 1 after).

This is the "reverse fold": the first-registered component wraps all the
others, so it runs first on the way in and last on the way out.

## Minimal experiment

Run the W9 `Part11_3_FrameworkSource` lab:

```
dotnet run --project src/Part11_3_FrameworkSource
# With FrameworkSource:FaultInjectionEnabled=true and a lab token:
curl -H "X-Lab-Token: <token>" http://localhost:5029/lab/pipeline
```

The `/lab/pipeline` endpoint returns the `before:*` markers that the
`PipelineMiddleware("outer")` and `PipelineMiddleware("inner")` instances
wrote into `HttpContext.Items` before calling `next`. The `after:*` markers
are written after the endpoint returns, which is why the endpoint only sees
the `before` set — the response has not yet begun its reverse journey through
the fold.

## Callout to a real W1-W9 problem

The W1 `Step03_MiddlewarePipeline` lab intentionally registers authentication
after authorization to show that `UseAuthorization` without a prior
`UseAuthentication` produces a `HttpContext.User` with no claims — the
authorization policy evaluates an anonymous principal and fails. The reverse
fold explains it: `UseAuthentication` must wrap `UseAuthorization` so that the
authenticate step sets `User` before the authorize step reads it.

Another callout: `UseExceptionHandler` must be one of the first components so
it wraps the entire inner pipeline. If it is registered after `UseRouting`,
an exception thrown during routing is not caught by it.

## Pinned source links

- `ApplicationBuilder.Build`:
  <https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Http/Abstractions/src/Builder/ApplicationBuilder.cs>
- `UseMiddlewareExtensions`:
  <https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Http/Abstractions/src/Builder/UseMiddlewareExtensions.cs>

Verify the tag matches the running `Microsoft.AspNetCore.Http.Abstractions`
assembly via Source Link before citing a specific line.
