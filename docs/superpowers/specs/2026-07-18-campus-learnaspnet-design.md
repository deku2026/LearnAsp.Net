# Campus LearnAsp.Net — Design Spec

**Date:** 2026-07-18
**Status:** Approved (brainstorming §1–§3)
**Repo:** `C:\MyFile\ArcForges\LearnAsp.Net`
**Docs source of truth:** `C:\MyFile\ArcForges\ArchitectureDesign\ASP.NetStudy\`
**Reference only (patterns, not domain):** `C:\MyFile\ArcForges\eShop`

---

## 1. Goals

Fill each of the **31** placeholder Web projects so that it is a **complete, runnable lab** matching its paired ASP.NetStudy guide: knowledge points, APIs where the doc requires them, local tests, and CI.

**Domain:** Campus academic administration (校园教务), not e-commerce. eShop is used only for infrastructure/patterns (Aspire, Outbox, ServiceDefaults, functional test host).

**Success criteria:**

- Every lab: `dotnet run` works on its fixed port (5001–5031).
- Every lab (from wave implementation onward): `WebApplicationFactory` integration tests pass.
- CI (Linux/macOS/Windows): restore + build Debug/Release + **test** + pre-commit.
- Knowledge coverage tracks each guide’s **验收** checklist; no hollow “hello world” shells after a wave ships.
- License-safe stack: no new MediatR / AutoMapper / MassTransit v9 / FluentAssertions defaults.

---

## 2. Decisions (locked)

| Decision | Choice |
|----------|--------|
| Business domain | **Campus** (students, courses, sections, enrollments, notices, college tenant) |
| Code organization | **Independent full labs + thin shared kernel** |
| Shared kernel (W1–W2) | `Campus.Contracts` + `Campus.Testing` only |
| First delivery wave | **W1+W2 = Step01–Step08** |
| Testing (W1–W2) | Per-step WAF integration tests; CI runs `dotnet test` |
| External deps (W1–W2) | None required at runtime; Seq optional if up |
| Git | Branch `feature/campus-w1-w2-steps01-08`; worktree `.worktrees/campus-w1-w2`; push feature branch (no merge to main unless asked) |

---

## 3. Architecture

### 3.1 Repository layout

```text
LearnAsp.Net/
├── src/
│   ├── Campus.Contracts/          # DTOs, ErrorCodes, event name constants
│   ├── Campus.Testing/            # WAF base, TestAuthHandler, HTTP helpers
│   ├── Step01_HostStartup/ … Step10_…/
│   └── Part03_1_… / … / Part13_Summary/
├── tests/
│   ├── Step01_HostStartup.Tests/ … (per implemented step)
│   └── (later Part*_*.Tests)
├── docs/superpowers/
│   ├── specs/
│   └── plans/
├── Directory.Packages.props       # CPM — versions pinned as packages are introduced
├── LearnAspNet.slnx
└── .github/workflows/*            # + dotnet test
```

### 3.2 Lab independence rules

1. Each `src/StepNN_*` or `src/PartNN_*` remains a **`Microsoft.NET.Sdk.Web`** executable.
2. A lab may reference `Campus.Contracts` and (tests only) `Campus.Testing`.
3. A lab must **not** require another Step/Part project to compile or run.
4. In-memory implementations behind small interfaces (`ICourseRepository`, `IEnrollmentService`) so later waves swap PostgreSQL without rewriting endpoints.
5. `public partial class Program;` retained for WAF.

### 3.3 Domain mapping (docs → Campus)

| Doc / eShop concept | Campus concept |
|---------------------|----------------|
| Order / Task | **Enrollment** (选课申请) |
| Inventory stock | **CourseSection.Capacity / SeatsRemaining** |
| Notifications module | **CampusNotice** |
| Tenant | **College** |
| Basket (later) | Draft schedule in Redis |
| Catalog | Courses + Sections |
| Identity | JWT (early) → Keycloak OIDC (Part05) |

### 3.4 Logical model

```text
College 1──* Student
College 1──* Course 1──* CourseSection
Student *──* CourseSection  via Enrollment
Enrollment ──► CampusNotice (side effect / later integration event)
```

| Entity | Key fields | Invariants |
|--------|------------|------------|
| College | Id, Code, Name | Code unique |
| Student | Id, CollegeId, StudentNo, DisplayName, Email | StudentNo unique per college |
| Course | Id, Code, Title, Credits | Code globally unique |
| CourseSection | Id, CourseId, Term, Capacity, SeatsRemaining, RowVersion | SeatsRemaining in [0, Capacity] |
| Enrollment | Id, StudentId, SectionId, Status, CreatedAt, IdempotencyKey? | One active enrollment per student+section; Confirmed decrements seats |
| CampusNotice | Id, UserId?, Title, Body, CreatedAt | Append-only |

**EnrollmentStatus:** `Pending` → `Confirmed` | `Rejected` | `Waitlisted` | `Cancelled`

### 3.5 HTTP conventions

- Base (Step04+): `/api/v1/...`
- Errors: RFC 9457 ProblemDetails + extension member `errorCode` (values from `Campus.Contracts.ErrorCodes`)
- Auth (Step07+): Bearer JWT; claims `sub`, `role`, `college_id`
- Correlation: `X-Correlation-ID` (Step08)

### 3.6 Core API surface (stable contracts)

Introduced fully at Step05; later parts version/enhance, do not silently rename.

| Method | Path | Auth (from Step07) | Notes |
|--------|------|--------------------|-------|
| GET | `/api/v1/courses` | optional | filter `q` |
| GET | `/api/v1/courses/{id}` | optional | |
| POST | `/api/v1/courses` | Admin | |
| GET | `/api/v1/sections` | optional | |
| POST | `/api/v1/sections` | Admin | body includes Capacity |
| GET | `/api/v1/enrollments` | User | “mine” or filter |
| POST | `/api/v1/enrollments` | User | validate capacity; may Waitlist |
| POST | `/api/v1/enrollments/{id}/cancel` | User (owner) | restore seat if Confirmed |
| GET | `/api/v1/enrollments/stream` | User | SSE (Step05) |
| GET | `/me` | User | claims dump (Step07) |
| GET | `/health/live` | anonymous | process up (Step08) |
| GET | `/health/ready` | anonymous | readiness (Step08) |
| GET | `/openapi/v1.json` | anonymous | when OpenAPI enabled |

Step05 also exposes **Controller** twin for courses: `CoursesController` under same routes or `/api/v1/mvc/courses` if conflict — prefer **same resource via Controller on `/api/v1/courses` only if Minimal is moved to enrollments-only**; default: Minimal owns `/api/v1/*`, Controller demo at `/api/controller/v1/courses`.

---

## 4. Wave plan (full roadmap)

| Wave | Projects | Outcome |
|------|----------|---------|
| **W1+W2 (this delivery)** | Step01–08 | Host→DI→middleware→routing→Campus CRUD→validation→JWT→Serilog/health + tests + CI test |
| W3 | Step09–10 | Testcontainers PG; Kestrel + typed HttpClient resilience; Capstone1 complete |
| W4 | Part03_1–4 | API maturity, modular monolith modules, structure, arch tests |
| W5 | Part04_1–3 | EF Core + PG schema, HybridCache/Redis, multi-tenant college filter |
| W6 | Part05_1–2 | Keycloak, policies, BFF/SPA |
| W7 | Part06_1–2, Part07 | Outbox patterns, RabbitMQ, multi-service + YARP/gRPC Capstone3 |
| W8 | Part08–10 | OTel, troubleshooting drill, Docker/CI deploy samples, Aspire AppHost |
| W9 | Part11–13 | Perf/AOT/source notes, electives, summary checklist |

---

## 5. W1+W2 detailed deliverables (Step01–08)

### Step01 — HostStartup (5001)

- Teach: `WebApplication.CreateBuilder` → configure → `Build` → `Run`; content root / environment; `IHostedService` heartbeat; graceful shutdown awareness.
- Endpoints: `GET /`, `GET /env` (env name, content root).
- Hosted service: logs heartbeat on interval; respects `StoppingToken`.
- Tests: factory starts app; `/` 200; `/env` contains environment name.

### Step02 — DIConfigOptions (5002)

- Teach: Transient/Scoped/Singleton instance IDs; captive dependency demonstration endpoint (safe demo, documented); `IOptions` / `IOptionsSnapshot` / `IOptionsMonitor`; `ValidateOnStart` on `CampusOptions`.
- Endpoints: `GET /di-demo`, `GET /options`.
- Tests: di-demo returns distinct/same IDs per lifetime rules; invalid options fail host start.

### Step03 — MiddlewarePipeline (5003)

- Teach: `Use`/`Run`/`Map`; ordering; timing middleware writes `X-Elapsed-ms`; exception middleware → ProblemDetails-shaped JSON.
- Endpoints: `GET /ok`, `GET /boom`.
- Tests: `/ok` has timing header; `/boom` non-500-leak of stack in Production-like config (Development may detail).

### Step04 — RoutingEndpoints (5004)

- Teach: `MapGroup("/api/v1")`; route constraints; `WithName` + `LinkGenerator`.
- Endpoints: course/section route skeleton (may return in-memory stubs).
- Tests: bad guid constraint → 404; named link generation non-empty.

### Step05 — MinimalApiVsController (5005)

- Teach: Minimal CRUD + endpoint filter + SSE; one Controller resource rewrite.
- Full in-memory Campus enrollments/courses/sections API (see §3.6).
- SSE: enrollment status stream.
- Tests: create course/section/enrollment; cancel; filter short-circuit; SSE content-type.

### Step06 — BindingValidationProblemDetails (5006)

- Teach: built-in validation + DataAnnotations; one FluentValidation cross-field rule (e.g. Capacity ≥ 1 and Term non-empty with custom message); ProblemDetails + `errorCode`.
- Same API as Step05 with stricter validation.
- Tests: invalid bodies → 400 + expected `errorCode`; valid → 201/200.

### Step07 — AuthnAuthzEntry (5007)

- Teach: JWT Bearer; `UseAuthentication`/`UseAuthorization` order; policies (`AdminOnly`, `CanEnroll`); resource-ish “own enrollment” check where simple.
- Protect mutating enrollment/course endpoints; `GET /me`.
- Dev signing key from config/user-secrets pattern.
- Tests: anonymous → 401; wrong role → 403; TestAuthHandler → 200.

### Step08 — LoggingErrorsHealth (5008)

- Teach: Serilog structured logging; correlation id middleware; `IExceptionHandler`; `/health/live` vs `/health/ready`.
- Seq sink optional (`SEQ_URL` / config); failure to connect must not crash Development startup.
- Tests: live 200; ready 200 with in-memory deps; exception endpoint returns ProblemDetails without dumping secrets.

---

## 6. Shared libraries

### 6.1 Campus.Contracts

- TFM: `net10.0` class library.
- No framework web/EF references.
- Contents:
  - `ErrorCodes` static class (string constants).
  - Request/response DTOs used across labs.
  - `EnrollmentStatus` enum.
  - Integration event **name** constants only (payload types may live here as POCOs).

### 6.2 Campus.Testing

- References: `Microsoft.AspNetCore.Mvc.Testing`, test SDK packages as needed.
- `CampusWebApplicationFactory<TEntry> : WebApplicationFactory<TEntry>`.
- `TestAuthHandler` + extension `CreateAuthenticatedClient(...)`.
- Helpers to read ProblemDetails / `errorCode`.

---

## 7. Packages (CPM) — introduce when first needed

| Package area | First step | Notes |
|--------------|------------|-------|
| xUnit, Test SDK, coverlet | tests | |
| Microsoft.AspNetCore.Mvc.Testing | Campus.Testing | |
| FluentValidation (+ DependencyInjection) | Step06 | |
| Microsoft.AspNetCore.Authentication.JwtBearer | Step07 | |
| Serilog.AspNetCore, Serilog.Sinks.Seq | Step08 | |
| OpenAPI + Scalar.AspNetCore | Step06 or Step08 | prefer built-in OpenAPI where enough |

Exact versions pinned at implementation time to current stable on nuget.org for net10.

**Forbidden defaults:** MediatR, AutoMapper, MassTransit 9+, FluentAssertions (use xUnit asserts or Shouldly if needed later).

---

## 8. Storage strategy by wave

| Wave | Storage |
|------|---------|
| W1–W2 | Thread-safe in-memory (`ConcurrentDictionary`) behind interfaces |
| W3 Step09 | Testcontainers PostgreSQL |
| W5 Part04 | Real EF Core 10 + Npgsql; database `campus` / schemas per module later |
| W5 Part04.2 | Redis HybridCache |
| W7 | Per-service DBs + RabbitMQ outbox |

### 8.1 Target PostgreSQL schema (Part04 — design freeze)

```text
colleges(id, code, name, ...)
students(id, college_id, student_no, display_name, email, ...)
courses(id, code, title, credits, ...)
course_sections(id, course_id, term, capacity, seats_remaining, xmin/row_version, ...)
enrollments(id, student_id, section_id, status, idempotency_key, created_at, ...)
campus_notices(id, user_id, title, body, created_at, ...)
outbox_messages(id, type, payload, occurred_on, processed_on, ...)  -- Part06
```

Indexes: unique `(college_id, student_no)`; unique `(student_id, section_id)` where status not Cancelled; keyset on `(created_at, id)` for enrollment lists.

Connection string (local Docker already provisioned):

```text
Host=localhost;Port=5432;Database=campus;Username=dotnet;Password=dotnet_dev
```

(Create DB `campus` in W3/W5; until then apps may use `dotnet_dev` with table prefix or dedicated DB creation in migrate step.)

---

## 9. CI / quality gates

Existing: Linux / Windows / macOS build Debug+Release + pre-commit; CodeQL.

**Add:**

```text
dotnet test LearnAspNet.slnx -c Release --no-build
```

(or test after build in same job). No Docker services required for W1–W2.

Local: `dotnet test`, `dotnet format`, pre-commit hooks unchanged in spirit.

---

## 10. Git workflow

1. Create worktree: `.worktrees/campus-w1-w2` on branch `feature/campus-w1-w2-steps01-08`.
2. Commit cadence: shared kernel → each Step green (code+tests) → CI yaml → docs.
3. `git push -u origin feature/campus-w1-w2-steps01-08`.
4. Do **not** merge to `main` or open PR unless user requests.

---

## 11. Out of scope for W1–W2

- EF Core, migrations, Redis, RabbitMQ, Kafka, Keycloak
- Aspire AppHost / ServiceDefaults project split
- Testcontainers
- YARP, gRPC, multi-process Capstone3
- NativeAOT lab content
- Duplicating full eShop solution structure

These remain specified at roadmap level in §4 and §8 for later waves.

---

## 12. Traceability

| Lab project | Guide file |
|-------------|------------|
| Step01_HostStartup | 步骤1-承载与启动模型-完整实施指南.md |
| Step02_DIConfigOptions | 步骤2-依赖注入-配置-Options-完整实施指南.md |
| Step03_MiddlewarePipeline | 步骤3-中间件管道-完整实施指南.md |
| Step04_RoutingEndpoints | 步骤4-路由与终结点-完整实施指南.md |
| Step05_MinimalApiVsController | 步骤5-MinimalAPI与Controller-完整实施指南.md |
| Step06_BindingValidationProblemDetails | 步骤6-模型绑定-校验-ProblemDetails-完整实施指南.md |
| Step07_AuthnAuthzEntry | 步骤7-认证授权接入点-完整实施指南.md |
| Step08_LoggingErrorsHealth | 步骤8-日志-错误处理-健康检查-完整实施指南.md |
| … | … (remaining 23 map 1:1 per README port table) |

Definition of Done for a lab = that guide’s **验收** checkboxes, implemented in Campus vocabulary, with automated tests for the behavioral claims.

---

## 13. Risks and mitigations

| Risk | Mitigation |
|------|------------|
| 31 labs diverge on DTO shapes | Contracts package + this spec’s API table |
| Over-sharing too early | Only Contracts+Testing until Part03 |
| CI time growth | W1–W2 tests are in-memory/fast; containers only from W3 |
| Seq/Docker down while developing Step08 | Optional sink; health ready does not require Seq |
| Scope creep into Part04 in W2 | Explicit out-of-scope list §11 |

---

## 14. Next step after spec approval

1. User reviews this file.
2. Invoke **writing-plans** → `docs/superpowers/plans/2026-07-18-campus-w1-w2-steps01-08.md` with bite-sized TDD tasks.
3. Create git worktree; implement; push feature branch.
