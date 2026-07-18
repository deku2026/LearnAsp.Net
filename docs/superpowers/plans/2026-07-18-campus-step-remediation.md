# Campus Step01–10 Remediation Plan

**Goal:** Fill 验收 gaps for Step01–10 per ASP.NetStudy guides; no new features, only doc-required items.

**Branch:** `feature/campus-step-remediation` · Worktree: `.worktrees/campus-step-remediation`

## P0 (critical correctness/security)
- Step06: add `InterceptorsNamespaces` to csproj (built-in validation silently no-op)
- Step10: pin `KnownProxies` (don't clear — spoofing hole)
- Step09: EF migrations instead of `EnsureCreated`; Respawn ignores `__EFMigrationsHistory`

## P1 (doc-required demos)
- Step01: PeriodicTimer; per-tick try/catch; `IServiceScopeFactory` scoped demo
- Step02: captive dependency; Scrutor decorator+scan; keyed services; `IEnumerable<T>`; user-secrets; `IOptionsMonitor.OnChange`
- Step03: `IMiddleware` factory style; `UseWhen`/`MapWhen`; `MapShortCircuit`; auth-order experiment
- Step04: custom `IRouteConstraint`; nested `MapGroup` + group filter
- Step05: extension-method org; `TypedResults`/`Results<,>`; `[AsParameters]`/`TryParse`; `TypedResults.ServerSentEvents`
- Step06: custom `ValidationAttribute`; `IValidatableObject`; in-lab `IExceptionHandler`
- Step07: fallback policy
- Step08: `[LoggerMessage]` source-gen; real DB readiness check

## P2 (polish)
- Step10: `DelegatingHandler`; HTTP/2 endpoint awareness
- Step09: Bogus faker (optional)

## Verification
```pwsh
dotnet test LearnAspNet.slnx -c Release
pre-commit run --all-files
```
