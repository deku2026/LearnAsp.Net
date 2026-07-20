# DI scope and IDisposable: why resolving scoped from root and leaking transients break

## The question

Why does the DI container not just let you resolve any service from anywhere?
What actually goes wrong when you resolve a scoped service from the root
provider, and why are disposable transients a leak risk that the scope has to
own?

## Public entry point

`IServiceProvider.GetService(Type)` on the root provider, on a scoped
provider, or via constructor injection. The behavior differs by lifetime.

## Key types

- `ServiceDescriptor` (serviceType, implementationType, lifetime)
- `ServiceProvider` (root; caches singletons)
- `ServiceScope` / `IServiceScopeFactory` (scoped; caches scoped instances)
- `IServiceProviderIsService`
- `IDisposable` / `IAsyncDisposable` (disposal chain)

## Happy-path call chain

1. Registration: `AddSingleton`, `AddScoped`, `AddTransient` append
   `ServiceDescriptor` entries to the service collection.
2. `Build()` creates the root `ServiceProvider`.
3. Singleton resolution: the root provider creates the instance once and
   caches it. Disposable singletons are tracked in the root's disposal list.
4. Scoped resolution: `IServiceScopeFactory.CreateScope()` returns a
   `ServiceScope` whose provider creates and caches scoped instances. The
   scope owns disposal of scoped instances AND disposable transients resolved
   within it.
5. Transient resolution: a new instance every time. If it is disposable, the
   resolving provider (root or scope) tracks it for disposal.

The scope ownership rule is the key: a disposable transient resolved from a
scope is disposed when the scope is disposed, not when the caller is done with
it. If you resolve a disposable transient from the root, it lives until the
root is disposed (application shutdown) — a leak.

## Minimal experiment

Run the W9 `Part11_3_FrameworkSource` lab:

```
curl -H "X-Lab-Token: <token>" http://localhost:5029/lab/di
```

The `/lab/di` endpoint calls `IServiceScopeFactory.CreateScope()` twice and
returns the hash codes of both scopes. They differ because each scope has its
own provider. Two requests to `/lab/di` return different scope hashes,
proving scoped services do not span requests. The W1 `Step02_DIConfigOptions`
lab does the same with a scoped `IdGenerator` and shows the same scope id
within a request but different ids across requests.

## The two failure modes

### Resolving a scoped service from the root

If you call `app.Services.GetRequiredService<MyScopedService>()` at startup,
the root provider creates a singleton of what was registered as scoped. This
is a "captive dependency": the scoped service is captured by the root and
lives for the entire application lifetime. If it holds a database context or
a tenant state, every request that resolves the same scoped service from a
scope gets a different instance, while the root-held one is stale or shared.

The W4 `Part03_4_ArchTesting` lab has a NetArchTest rule that forbids the
application layer from referencing `IServiceProvider` directly, precisely to
prevent this pattern. The W4 `Part03_3` layered project structure puts DI
registration in the Infrastructure layer so the Application layer never
constructs scopes itself.

### Leaking disposable transients

A transient registered with `AddTransient<T>` where `T : IDisposable` is
created fresh each resolve. The resolving provider tracks it. If you resolve
it from a scope, the scope disposes it — fine. If you resolve it from the
root (e.g. in a hosted service that uses `app.Services`), it is tracked in the
root and disposed at shutdown — effectively a leak for the process lifetime.

The W2 `Step02_DIConfigOptions` lab covers the captive-dependency problem with
Scrutor's `Decoractor` and the `IEnumerable<T>` resolution that avoids it. The
W3 `Step09_IntegrationTesting` lab's `WebApplicationFactory` disposal chain
disposes the scope that the test client's requests ran in, which disposes
scoped database contexts — if a test leaks a scope, the context leaks.

## Callout to a real W1-W9 problem

The W7 `Part06_1_MessagingPatterns` lab's Outbox relay is a hosted service
that uses `IServiceScopeFactory` to create a scope per relay iteration. If it
resolved the scoped `OutboxContext` from the root, every relay iteration would
share the same context, and the `FOR UPDATE SKIP LOCKED` query would never see
new rows committed by other relays in their own scopes. The scope-per-iteration
pattern is what makes concurrent relays independent.

The W5 `Part04_3_MultiTenant` lab's `TenantContext` is scoped so each request
gets its own tenant. Resolving it from the root would make all requests share
one tenant — a cross-tenant data leak.

## Pinned source links

- `ServiceProvider`:
  <https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceProvider.cs>
- `ServiceScope`:
  <https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceScope.cs>
- `ServiceDescriptor`:
  <https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceDescriptor.cs>

Verify the tag matches the running `Microsoft.Extensions.DependencyInjection`
assembly via Source Link.
