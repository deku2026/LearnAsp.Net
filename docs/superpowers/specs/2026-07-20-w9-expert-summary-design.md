# W9 expert + summary design

> Branch: `codex/w9-expert-summary`
> Worktree: `.worktrees/campus-w9-expert-summary`
> Base: `origin/main` (W8 merged in PR #15, commit 59ed3d0)
> Baseline verified before this spec: Release build 0 warnings / 0 errors, generic
> tests 129/129 passing on Windows.
> Source plan: `C:\MyFile\ArcForges\LearnAsp.Net-Comprehensive-Review-and-W9-Plan.md`

## 1. Goal

W9 is the closing wave. It completes the final 5 of 31 labs and closes four
loops the plan names explicitly:

1. Prove, with measured data, why the system is fast or slow.
2. Understand the JIT / R2R / Native AOT deployment trade-off.
3. Trace already-learned framework behavior back to source.
4. Fill the remaining infrastructure (Kafka, Mailpit) and produce a reusable,
   machine-verifiable self-check and evidence system.

When W9 merges, all 31 labs are non-placeholder, the 10-container Docker matrix
has role-appropriate real evidence, and the README/Master Plan/Project Overview
no longer carry stale status.

## 2. Scope and non-goals

In scope:

- `src/Part11_1_PerformanceAdvanced` and its benchmark + test projects.
- `src/Part11_2_NativeAotTrim`, `src/Part11_2_NativeAotTrim.Endpoints`, and the
  out-of-solution `samples/Part11_2.TrimWarningLab`.
- `src/Part11_3_FrameworkSource` with 5 Mermaid diagrams, 3 source-read
  articles, and a lifecycle demo.
- `src/Part12_ElectiveBranches` with Kafka and Mailpit/Quartz sub-labs.
- `src/Part13_Summary` as a read-only capability index backed by a versioned
  manifest.
- `benchmarks/Part11_1_PerformanceBenchmarks`, `deploy/aot`, `deploy/k6/w9-*`,
  `scripts/performance`, `scripts/aot`, `scripts/w9`, and
  `docs/performance|framework-source|summary`.
- CPM additions: BenchmarkDotNet, Confluent.Kafka, Quartz.NET.
- Updating `LearnAspNet.slnx`, `LearnAspNet.GenericTests.slnx`, README, Master
  Plan, Project Overview, and the CI workflows so the new test projects run on
  the right platforms with the right trait filters.

Non-goals:

- No SignalR/Orleans/Dapr/GraphQL/AI directions. The plan §11.4 rules them out
  because they add large new systems that do not serve the closing goal.
- No EF Core AOT experiment. §9.2 keeps the AOT lab on in-memory read-only data
  to avoid the experimental EF AOT surface.
- No business service depending on Adminer/RedisInsight. They are operations UI
  and get readiness-only acceptance per §4 of the plan.
- No "AOT is universally faster" claim. The three-form comparison is a
  trade-off, not a ranking (§9.4).
- No reading framework source off `main`. Source links pin to the .NET 10
  commit (§10.2).

## 3. Cross-cutting conventions (binding for all 5 labs)

These are derived from the existing W1-W8 code and the plan's engineering rules
(§5, §6). Every lab follows them.

- **Project model**: `Microsoft.NET.Sdk.Web` exe per lab, file-scoped namespaces
  (warning), `public partial class Program;` for `WebApplicationFactory<Program>`.
- **CPM**: all package versions in `Directory.Packages.props`. No inline
  versions.
- **Shared libraries**: reuse `Campus.ServiceDefaults`
  (`AddCampusServiceDefaults` + `MapCampusDefaultEndpoints`) for any lab that
  emits OTel; reuse `Campus.Contracts` DTOs where a Campus-shaped payload is
  needed; reuse `Campus.Testing` only where its `TestAuthHandler` is required
  (W9 labs generally do not need auth).
- **Health**: every lab exposes `/health/live` (process) and `/health/ready`
  (dependencies) via `MapCampusDefaultEndpoints`. Live never checks deps.
- **Fault / destructive endpoints**: live under `/lab/*`, disabled by default
  (`Performance:FaultInjectionEnabled=false` etc.), require constant-time
  `X-Lab-Token` comparison, bounded inputs via `Math.Clamp`, no unbounded
  retention. Pattern copied from `Part08_2_TroubleshootingProcess`.
- **Disposables**: every `new IDisposable`/`IAsyncDisposable` answers "who
  releases on the success path, who releases on the exception path, has
  ownership transferred?". `WithWebHostBuilder` returns a new factory; the
  original is disposed separately. Testcontainers, `NpgsqlConnection`,
  `HttpClient`, `HttpResponseMessage`, `Process` all released on all paths
  (§5.7).
- **Configuration**: `appsettings.json` carries defaults; tests override via
  `AddInMemoryCollection`. `ConnectionStrings:*` for infrastructure.
- **Tests**: xunit.v3, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`.
  Cross-platform generic tests use plain `[Fact]`. Tests needing Docker/infra
  use `[Trait("Category","Docker")]` and `SkippableFact` with matching skip
  attributes; declared-available infra that fails must fail, not skip (§5.5,
  §5.6).
- **CI split**: Windows/macOS run `LearnAspNet.GenericTests.slnx` with
  `--filter-not-trait Category=Docker`. Linux runs the full
  `LearnAspNet.slnx` with `--max-parallel-test-modules 1`. Benchmark projects
  build on all OS but never run under `dotnet test`. AOT publish + native
  smoke + AOT image smoke run on Linux only.
- **Scripts**: bash, `set -euo pipefail`, `#!/usr/bin/env bash` shebang, LF,
  executable bit verified in the git index. They check their own prerequisites
  (Debian, user, dotnet tools, Docker) and use health endpoints, never fixed
  sleeps, for readiness (§5.11). They record environment (SDK, OS, CPU,
  commit) in evidence output.
- **Telemetry hygiene**: no token, cookie, password, connection string, request
  body, email, student ID, or arbitrary URL in logs/spans/metrics. Metric tags
  are bounded cardinality. Health traffic excluded from traces (already in
  `Campus.ServiceDefaults`).
- **Secrets**: test credentials are isolated-environment random values; no real
  SMTP addresses (use `example.test`); `.env` is example-only; no production
  secrets in repo.
- **Pre-commit**: every file lands LF with a final newline. K8s YAML uses
  `--allow-multiple-documents` (already configured). Shell scripts get
  `check-executables-have-shebangs`. Run `pre-commit run --all-files` twice;
  both passes must be clean (§5.8).

## 4. Lab designs

### 4.1 Part11_1_PerformanceAdvanced (port 5027)

Architecture: one `Microsoft.NET.Sdk.Web` app reusing
`Campus.ServiceDefaults`. Endpoints:

- `GET /api/performance/runtime` -> `IsServerGC`, CPU count, framework,
  process architecture. No host-sensitive data.
- `POST /api/performance/course-codes/parse` -> parse a Campus course code.
  Two implementations behind a query flag: `?impl=baseline` (string/split) and
  `?impl=span` (Span/stackalloc for safe sizes, ArrayPool for larger). Both
  return identical results; tests prove equivalence.
- `POST /api/performance/serialize` -> source-generated
  `JsonSerializerContext` over a real enrollment-summary DTO from
  `Campus.Contracts`-shaped records. `?impl=reflection` vs `?impl=sourcegen`.
- `GET /api/performance/payload` -> bounded dynamic text for Brotli/Gzip
  comparison. Response compression enabled with a sensible minimum size; never
  compress already-compressed payloads.
- `MapStaticAssets` serves a small report page; tests check fingerprinted
  pre-compressed assets.
- `GET /health/live`, `GET /health/ready`.
- `/lab/threadpool/blocking` and `/lab/threadpool/async` behind the token gate,
  same business semantics, for before/after comparison.
- `/lab/gc/allocate` behind the token gate, bounded, releases on DELETE.

BenchmarkDotNet project `benchmarks/Part11_1_PerformanceBenchmarks`:

- `JsonSerializationBenchmarks`: reflection metadata path vs STJ source
  generated. Captures Mean, Allocated, Gen0/1/2.
- `CourseCodeParsingBenchmarks`: string/split baseline vs Span, ArrayPool only
  beyond stackalloc safety.
- Release config, no network/db/disk I/O, consume results to prevent DCE,
  `MemoryDiagnoser` enabled. Project builds in CI but is excluded from
  `dotnet test`. Baseline summaries (with env: CPU/OS/SDK/runtime/commit) are
  committed; raw large artifacts go under `artifacts/` (gitignored).

Scripts (Linux/WSL only, bash, prereq-checked):

- `scripts/performance/run-threadpool-starvation-lab.sh`: start the app, k6 the
  blocking endpoint 60-120s while collecting
  `dotnet.thread_pool.thread.count`/`queue.length`/`work_item.count` and CPU via
  `dotnet-counters`, capture `dotnet-stack`, repeat on async, emit a
  before/after summary (request count, error rate, P50/P95/P99, thread count
  peak, queue peak), delete sensitive artifacts, keep a redacted summary.
- `scripts/performance/run-gc-modes-lab.sh`: start three separate processes
  (Workstation GC, Server GC + DATAS, Server GC + `DOTNET_GCDynamicAdaptationMode=0`)
  with identical CPU/memory/payload/duration, record working set, heap size,
  allocation rate, Gen0/1/2, pause time, P95/P99, throughput. Multi-run,
  report median + dispersion. GC mode is process-start config, not hot-swappable.
- `deploy/k6/w9-performance.js`: PR-smoke thresholds (error rate <1%, P95/P99
  generous anti-regression) for the async endpoint.

Generic tests (cross-platform, `[Fact]`):

- Lab default-off and 404/401 without token.
- Input bounds (`Math.Clamp`) enforced.
- Memory endpoint retains and releases.
- Source-generated JSON round-trip equals reflection result.
- Compression negotiation picks Brotli/Gzip correctly.
- Static asset has ETag and pre-compressed variant.
- Course code parse: baseline and Span agree on a corpus including edge cases.
- Cancellation token aborts waits.
- All `HttpClient`/`HttpResponseMessage`/factory released.

Definition of done matches plan §8.8.

### 4.2 Part11_2_NativeAotTrim (port 5028)

Three pieces.

(a) `src/Part11_2_NativeAotTrim` — `CreateSlimBuilder`, Minimal API, HTTP-only
local lab, source-generated JSON, `PublishAot=true`, `IsAotCompatible=true`,
trim/AOT analyzers on. Zero IL2026/IL3050 in publish. In-memory read-only data
(no EF). Endpoints:

- `GET /api/courses/{code}` -> read-only course lookup.
- `POST /api/enrollments/validate` -> validation-only, no persistence.
- `GET /health/live`, `GET /health/ready`.
- `GET /api/runtime-shape` -> compile form (AOT/R2R/JIT flag), versions. No
  host info.

(b) `src/Part11_2_NativeAotTrim.Endpoints` — class library with `MapGroup`
endpoints, `EnableRequestDelegateGenerator=true`. Demonstrates that a
*referenced* library must opt in to RDG explicitly (the plan's pitfall #12).
The app references this library and routes through it.

(c) `samples/Part11_2.TrimWarningLab/` — intentionally broken sample, NOT in
`LearnAspNet.slnx` and NOT built by CI. A script
`scripts/aot/publish-trim-warning-lab.sh` publishes it and *expects* IL2026 /
IL3050, then a fixed variant shows the four-step fix order from §9.3:
eliminate reflection -> `DynamicallyAccessedMembers` -> propagate
`RequiresUnreferencedCode` -> `UnconditionalSuppressMessage` only when proven
safe. The teaching output is captured in `docs/performance/` (or
`docs/framework-source/`), not the main build.

`deploy/aot/Dockerfile`: Linux builder with the official native toolchain,
`dotnet publish -r linux-x64 -c Release`, final image is non-root chiseled
`runtime-deps` with no SDK/JIT/runtime, commit-SHA tag. Health probe is either
a `--health-probe <url>` mode in the AOT binary or an external platform probe
(no shell/curl installed back into the image, §9.6).

Three-form comparison script `scripts/aot/compare-publish-forms.sh`:
self-contained `linux-x64` for (1) JIT, (2) ReadyToRun, (3) Native AOT, all
with the same RID and self-contained flag so the size comparison is fair
(§9.4). Records publish size, main binary size, publish time, cold-start to
health ready, first request, steady-state latency, RSS/working set, 20+ cold
starts median/P95, SDK/OS/CPU. Conclusion is a trade-off write-up, not a
ranking.

Generic tests (JIT under WAF, cross-platform):

- Endpoint behavior, source-generated JSON body, validation Problem Details.
- Endpoint library registered; no dynamic endpoint discovery.
- Cancellation and bounds.
- `IsAotCompatible` and `PublishAot` flags set; analyzers enabled.

Linux-only tests (`Category=Docker` or a dedicated AOT trait):

- `dotnet publish -r linux-x64 -c Release` succeeds with no IL2026/IL3050 in
  the log.
- Native binary starts; health and business API smoke pass.
- AOT image builds, runs non-root, has no `dotnet` JIT runtime, stops cleanly.

Definition of done matches plan §9.8.

### 4.3 Part11_3_FrameworkSource (port 5029)

Hard outputs (§10.1): 5 Mermaid object/call-chain diagrams + 3 source-read
articles + 1 minimal lifecycle demo + 1 human grading rubric. "Read the
source" alone is not a deliverable.

Version pinning (§10.2): source links are commit permalinks to the .NET 10
runtime/aspnetcore/efcore tags matching the running assemblies, verified via
Source Link / F12. DI/Options/Config/Logging -> `dotnet/runtime`;
Hosting/pipeline/routing/Auth -> `dotnet/aspnetcore`; EF only where needed.

Five diagrams (Mermaid + prose), in `docs/framework-source/`:

1. DI: `ServiceDescriptor` -> call site -> resolver; root/scope/transient
   caches; disposable transient held by scope.
2. Middleware: `ApplicationBuilder` components, reverse fold, request forward /
   response reverse.
3. Routing: `EndpointRoutingMiddleware`, DFA matcher, `SetEndpoint`, authorize
   reads metadata, `EndpointMiddleware` executes.
4. Options: factory configure / post-configure / validate, cache, change token,
   monitor `OnChange`.
5. Auth: scheme provider, handler, `ClaimsPrincipal`, policy/requirement/
   handlers, challenge/forbid.

Three articles (in `docs/framework-source/`), each with: a concrete question,
public entry point, key types, happy-path call chain, a minimal experiment, a
callout to a real W1-W9 problem, pinned source links, no copy-pasted source.

1. "Why ASP.NET Core middleware is ordered: from reverse fold to two-way
   tunnel."
2. "Why routing, authentication, and authorization are layered: endpoint
   metadata lifecycle."
3. "DI scope and IDisposable: why resolving scoped from root and leaking
   transients break."

Lifecycle demo in `src/Part11_3_FrameworkSource`:

- `/lab/di` — same request scoped id, different across requests.
- `/lab/pipeline` — middleware before/after order.
- `/lab/endpoint-metadata` — custom metadata read by an authorization handler.
- `/lab/options` — controllable change token triggers `IOptionsMonitor.OnChange`.
- `/lab/auth` — authenticate / challenge / forbid paths.
- `/lab/lifecycle` — low-sensitivity events of the stages a request passes
  through.
- `/health/live`, `/health/ready`.

Tests prove the demo's behavior, not framework internals (no reflection over
private members). Each `/lab/*` is gated like the other labs.

Human grading rubric in `docs/summary/` (§10.6): the 10-stage lifecycle
(Kestrel bytes -> HostingApplication HttpContext/scope -> pipeline fold ->
routing match/SetEndpoint -> authentication sets User -> authorization reads
metadata -> endpoint/RDG executes -> DI/Options participate -> response
  reverse -> Kestrel writes back). Marked manual evidence, never faked as
  automated. Part13 references this rubric.

Definition of done matches plan §10.7.

### 4.4 Part12_ElectiveBranches (port 5030)

Two directions (§11.1): Kafka event stream + replay, and durable background
email with Mailpit. Both sub-labs live in the same `Part12_ElectiveBranches`
project, routed under `/api/kafka/*` and `/api/notifications/*`. Kafka and
Mailpit are Linux/Docker only; generic tests cover the non-Docker business
logic with fakes/in-memory.

Kafka sub-lab:

- Topics `campus.enrollment.activity.v1` and `campus.enrollment.activity.dlq.v1`.
- `POST /api/kafka/enrollment-activity` publishes an event keyed by
  tenant/student or enrollment id (verifies same-key partition ordering).
- Producer `acks=all`, idempotence on.
- Consumer group manual commit; offset committed only after business success
  -> at-least-once. Poison event -> DLT.
- `GET /api/kafka/status` reports topic/partition/offset/lag (bounded
  diagnostic fields).
- Second consumer group reads from the head to prove group independence; seek
  /reset demonstrates replay vs RabbitMQ work-queue semantics.
- Documented limits (§11.2): partition-only ordering, idempotent producer !=
  business exactly-once, consumer side effects still need idempotency/Inbox,
  retention/partition/key are long-term design, rebalance handling, poll loop
  must not block.

Mailpit/Quartz sub-lab:

- `POST /api/notifications/email` accepts a course notice, returns 202 + job
  location. `Idempotency-Key` prevents duplicate scheduling.
- PostgreSQL-persisted Quartz job store in a new isolated database
  `campus_w9_notifications` (matches the existing per-wave isolated-DB pattern:
  `campus_w7_*`, `campus_w8_*`; init added to `deploy/docker/postgres-init.sql`);
  jobs survive app restart. `[DisallowConcurrentExecution]` or a business lock
  prevents duplicate sends across two scheduler instances.
- Exponential backoff on failure; Mailpit Chaos simulates SMTP temp failure.
- Mailpit SMTP `localhost:1125`, Mailpit HTTP API `localhost:8025` for
  assertions (recipient, subject, HTML/text, correlation header). Do not
  assert random Date/Message-ID exact values.
- `GET /api/notifications/jobs/{id}`, `DELETE` to cancel unstarted jobs.
- Addresses use `example.test`; logs record job id/attempt/message id, never
  body or sensitive addresses.

Generic tests: idempotency-key dedup, job state transitions, backoff schedule
math, address validation (no real external), all with in-memory fakes (no
Docker). Linux-only tests (`Category=Docker`): real Kafka produce/consume,
partition order, two groups, commit-after-success, crash-replay, poison->DLT,
teardown with short retention / topic delete; real Mailpit SMTP + API
assertion, Chaos retry, persistence across app restart, two-instance no-double-
send, teardown of test mail + jobs.

Definition of done matches plan §11.5.

### 4.5 Part13_Summary (port 5031)

Not a placeholder doc shell. A read-only capability index backed by a versioned
manifest `docs/summary/capabilities.json` (and supporting
`capstones.json`, `infrastructure.json`, `evidence.json`) served via STJ
source generation:

- `GET /api/capabilities`, `GET /api/capstones`, `GET /api/infrastructure`,
  `GET /api/evidence`, `GET /health/live`, `GET /health/ready`, plus a static
  summary page.

Manifest content (§12.2): 31 labs (status, entry, port, core capability, test
project), W1-W9 dependency matrix, three Capstones, 16 golden threads mapped
to real code/tests, decision trees (security/data/messaging/observability/
deployment/performance/AOT), 10-container matrix with role-level acceptance,
auto vs manual evidence split, known non-production boundaries, upgrade/deps/
license check method.

Consistency tests `tests/Part13_Summary.Tests` (§12.3, all cross-platform
`[Fact]`):

- All 31 `src` labs exist and none still carry the placeholder marker.
- Ports 5001-5031 unique.
- Every lab has a test project or explicit manual/document evidence.
- Capability manifest has no duplicate ids; links resolve to existing files.
- All 10 Docker containers present in the infrastructure manifest.
- W6/W7/W8/W9 status in the manifest matches the git baseline (no stale
  "incomplete").
- README / Master Plan / Project Overview no longer carry the old "W6-W8 not
  done" text.
- All shell scripts LF + shebang + executable bit; K8s YAML multi-doc
  parseable; JSON/YAML final newline.

Full-stack acceptance script `scripts/w9/run-full-stack-acceptance.sh` (§12.4,
Linux/WSL): verifies Debian/user/Docker/Compose/dotnet, checks all 10
containers via `docker compose ps`, then per-container role-level checks
(Postgres connect+migration+transaction; Redis PING/SET/GET/TTL; RabbitMQ
management ready + W7 publish/consume/DLQ smoke; Kafka topic+produce+consume+
group+replay; Keycloak discovery/JWKS + isolated realm protocol smoke; Seq
write structured event + API query by correlation id; Aspire Dashboard OTLP
ingest + cross-service trace; Mailpit SMTP + API; Adminer HTTP readiness +
manual DB inspection; RedisInsight HTTP readiness + manual key/TTL inspection).
Emits JSON+Markdown evidence summary, cleans up test topics/realms/mail/db/
temp artifacts, never deletes persistent volumes unless `RESET_ALL=1`.

Definition of done matches plan §12.6.

## 5. CI design (plan §13)

- **Windows/macOS/Linux**: restore, Debug + Release build, generic tests
  (filtered), pre-commit, benchmark project builds (never runs), AOT project
  builds under normal `dotnet test` (JIT mode).
- **Linux-only**: Docker infra tests for Kafka/Mailpit and the W9 pieces; AOT
  `publish -r linux-x64 -c Release` with IL2026/IL3050 treated as failure;
  native binary smoke; AOT image build/smoke; artifact with size + startup
  summary; k6 PR-smoke with generous thresholds.
- **Performance CI on shared runners**: correctness + error budget + generous
  latency smoke + "benchmark is runnable". Strict P95/P99/throughput only on a
  fixed/self-hosted runner. No narrow ns thresholds on shared runners (§8.6,
  §13.4).
- Existing `LearnAspNet.GenericTests.slnx` gains the new generic test projects;
  `LearnAspNet.slnx` gains all new src + test + benchmark projects. The trim
  warning sample is excluded from both.

## 6. Commit plan (plan §6.1)

1. `feat(perf): add measurable performance lab and evidence tooling`
   (Part11_1 app + benchmarks + tests + k6 + scripts + CPM BenchmarkDotNet)
2. `feat(aot): add trim-safe Native AOT lab and publish verification`
   (Part11_2 app + endpoints lib + tests + trim sample + scripts + Dockerfile)
3. `docs(source): add framework source maps and lifecycle demo`
   (Part11_3 app + tests + 5 diagrams + 3 articles + rubric)
4. `feat(electives): add Kafka replay and durable Mailpit delivery labs`
   (Part12 app + tests + CPM Confluent.Kafka + Quartz.NET)
5. `docs(summary): add final capability matrix and acceptance`
   (Part13 app + manifest + consistency tests + full-stack script + README/
   Master Plan/Project Overview sync)

Each commit is self-contained: build + generic tests + pre-commit clean before
the next begins. Bot/CI fixes are separate commits, no history rewrites.

## 7. Infrastructure findings (verified in Debian WSL before implementation)

Recorded so the lab code uses the real environment, not guesses:

- **WSL**: Debian 13, user `sammiller`, .NET SDK 10.0.302, runtime 10.0.10,
  global tools present: `aspire.cli` 13.4.6, `dotnet-counters/dump/trace/
  gcdump/stack` 9.0.661903, `dotnet-ef` 10.0.10. k6 is run via
  `docker run --rm --network host grafana/k6:2.0.0` (matches the W8 script).
- **All 10 containers up** and verified: `dotnet-postgres` (5432, healthy),
  `dotnet-redis` (6380, healthy), `dotnet-rabbitmq` (5672/15672, healthy),
  `dotnet-keycloak` (8082), `dotnet-seq` (5341, healthy),
  `dotnet-aspire-dashboard` (18888/4317/4318), `dotnet-mailpit` (1125/8025,
  healthy, v1.30.4, HTTP API confirmed), `dotnet-kafka` (9094, healthy),
  `dotnet-adminer` (8081), `dotnet-redis-insight` (5540).
- **Kafka image is `apache/kafka:4.3.1`** (NOT bitnami). The CLI path is
  `/opt/kafka/bin/kafka-topics.sh` inside the container. EXTERNAL listener is
  `localhost:9094`. `KAFKA_AUTO_CREATE_TOPICS_ENABLE=true`,
  `KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0` (good for fast tests),
  `KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1` (single broker). Topic
  create/list/describe/delete verified working. Note: Kafka warns that topic
  names mixing `.` and `_` collide in metrics — W9 topics use only `.` and a
  `.v1` suffix (e.g. `campus.enrollment.activity.v1`), never `_` in the topic
  name. Tests create unique topics and delete them in teardown (or use short
  retention) so repeated runs do not accumulate.
- **Native AOT toolchain**: `clang` is NOT installed and the dev headers
  (`zlib1g-dev`, `libssl-dev`) are missing. `libssl3` and `zlib1g` runtime libs
  are present. Part11_2 AOT publish step will install
  `clang zlib1g-dev libssl-dev` (the .NET NativeAOT prerequisites for Debian)
  before `dotnet publish -r linux-x64`. This install is a one-time WSL setup
  step, documented in the AOT script, not something CI relies on (Linux CI
  installs its own toolchain per the workflow).
- **Postgres per-wave DB pattern**: existing `deploy/docker/postgres-init.sql`
  creates `campus_w7_catalog`, `campus_w7_enrollment`, `campus_w7_notices`,
  `campus_w8_troubleshooting`. W9 adds `campus_w9_notifications` (Quartz job
  store) to the same init file. The init runs against the running
  `dotnet-postgres` container (the script uses `\gexec`).
- **Compose file**: `~/Project/docker/docker-compose.yml`, `name: dotnet-stack`,
  single `dotnet-net` bridge network, named volumes for postgres/redis/
  rabbitmq/seq/keycloak/kafka. No profiles (full stack always up).

## 8. Risks and mitigations (plan §16, top items for W9)

- Performance numbers not reproducible -> every benchmark/script records env.
- Averages as performance -> P95/P99, error rate, throughput, saturation.
- BenchmarkDotNet doing I/O -> micro-benchmarks are CPU/memory hot paths only.
- Narrow thresholds on shared runners -> generous PR-smoke, strict only on
  fixed runners.
- Premature Span/Pool optimization -> clear baseline first, optimize with
  evidence.
- AOT warnings left in -> publish warnings are failures, not noise; only build
  is not enough, publish is the gate.
- Cross-OS AOT -> Linux native binary produced and tested on Linux only.
- Unfair JIT/R2R/AOT size compare -> same RID + self-contained for all three.
- Endpoint library RDG not enabled -> explicit `EnableRequestDelegateGenerator`
  in the referenced library, with a compile-evidence test.
- Reading source off `main` -> pinned commit permalinks.
- Kafka auto-commit -> manual commit after business success.
- Kafka idempotent producer misunderstood -> document consumer-side idempotency
  still required.
- Multi-instance scheduler double-send -> persistent store + cluster
  coordination + idempotency.
- Mailpit test only checks 202 -> assert real mail via Mailpit API.
- "Full-stack" making business depend on Adminer/RedisInsight -> they are
  operations UI, readiness-only acceptance.
- Summary going stale again -> manifest + tests check consistency against the
  real repo.

## 9. Verification before claiming done (plan §17)

- 31 labs non-placeholder.
- Generic .NET capabilities cross-platform; Docker/AOT/Kafka/Mailpit on Linux.
- 10 containers have role-level real evidence.
- Fault/destructive labs default-off, bounded, token-protected.
- Performance conclusions have data; AOT publish zero-warning with native
  binary running; source-read has 5 diagrams + 3 articles + 1 human lifecycle
  acceptance.
- Capability manifest matches the repo; README/Master Plan/Project Overview
  synced.
- Local full verification before PR.
