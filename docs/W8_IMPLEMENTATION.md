# W8 implementation and acceptance runbook

W8 extends the W7 Capstone instead of replacing it with disconnected samples.
`Campus.ServiceDefaults` gives every participating process the same resource
identity, W3C trace propagation, OpenTelemetry logs/metrics/traces, health
checks, and OTLP export policy.

## Part08_1 — OpenTelemetry

Run the two W8 processes with `OTEL_EXPORTER_OTLP_ENDPOINT` set to
`http://localhost:4317`. A call to
`/api/observability/work/{id}` produces:

- an inbound ASP.NET Core server span;
- a manual `observability.process-work` span;
- an outbound HTTP client span and propagated `traceparent`;
- a downstream server and manual span;
- correlated structured logs;
- `campus.operations`, `campus.failures`, and
  `campus.operation.duration` metrics.

The default sampler is parent-based and configurable by
`Observability:SamplingRatio`. Production should choose a ratio based on volume
and tail-sampling capability. Instrumentation records bounded domain categories,
not request bodies, access tokens, email addresses, student IDs, or arbitrary
URLs. Health traffic is excluded from traces to reduce noise.

Use `scripts/diagnostics/run-w8-observability-smoke.sh` in Debian WSL for the
real OTLP acceptance test. Open `http://localhost:18888`, select the W8 service,
and verify that the same trace contains both service names and that its logs
carry the same trace ID.

The bounded k6 workload in `deploy/k6/w8-observability.js` verifies that every
request returns W3C trace context while enforcing a 1% error budget, P95 below
250 ms, and P99 below 500 ms:

```bash
docker run --rm --network host \
  -e BASE_URL=http://127.0.0.1:6081 \
  -v "$PWD/deploy/k6:/scripts:ro" \
  grafana/k6:2.0.0 run /scripts/w8-observability.js
```

## Part08_2 — six-step production troubleshooting

1. **Metrics:** establish the incident start time, affected routes/revisions,
   error rate, request rate, saturation, and P50/P95/P99.
2. **Traces:** pivot from an exemplar or slow request and identify the first
   abnormal span, retries, fan-out, and dependency latency.
3. **Logs:** query only the affected trace and revision; reconstruct events from
   structured fields without relying on message text.
4. **Database:** inspect active queries, waits, locks, pool usage, timeouts,
   execution plans, and query count. Do not assume every timeout is a slow SQL
   statement.
5. **Runtime:** collect counters first, then a bounded trace/stack/GC dump, and
   only then a process dump when justified. Copy evidence before restarting.
6. **Deployment:** compare image digest, revision, configuration, secret
   references, probes, and dependency endpoints; stop or roll back the rollout
   when the new revision is correlated with impact.

The `/lab` scenarios are disabled by default. Enabling them also requires an
`X-Lab-Token`, and every input is bounded. They cover slow downstream work, CPU
saturation, blocking thread-pool work, managed-memory retention, slow
PostgreSQL, and connection-pool pressure. Run them only in an isolated lab.

Common fingerprints:

| Symptom | Counters/traces | First checks |
| --- | --- | --- |
| Thread-pool starvation | rising queue length, low completed work, blocked stacks | sync-over-async, locks, blocking I/O |
| CPU saturation | high CPU, runnable stacks, short dependency spans | hot methods, serialization, retry storms |
| Pool exhaustion | connection acquisition dominates spans | leaked connections, pool size, long transactions |
| GC pressure | allocation rate and heap grow, long GC pauses | retained types, payload buffering, cache bounds |
| Slow dependency | one downstream span dominates | dependency health, retries, timeout budget |
| Bad rollout | failures align to one revision | digest/config diff, readiness, immediate rollback |

`collect-runtime-evidence.sh` writes evidence outside source directories. Dumps
and traces may contain secrets or personal data and must follow incident
retention and access-control policy.

Use `run-w8-diagnostics-smoke.sh` to launch the bounded memory scenario and
prove that counters, a runtime trace, stacks, a GC dump, and a heap dump can all
be collected from the real Linux process. The script deletes those sensitive
artifacts after validating them.

## Part09 — deployment

The Dockerfile uses pinned .NET 10 noble SDK/runtime families, a multi-stage
publish, the non-root chiseled runtime user, an embedded .NET health probe, and
no shell in the final image. Compose builds immutable application images and
runs the complete learning infrastructure with internal DNS and persistent
volumes. Credentials are mandatory environment inputs and `.env` is ignored.

Kubernetes uses RollingUpdate, startup/liveness/readiness probes, requests and
limits, a read-only root filesystem, dropped Linux capabilities, HPA for the
gateway, a disruption budget, and a KEDA RabbitMQ queue scaler for enrollment.
PostgreSQL and
RabbitMQ are external production dependencies; do not place them in the same
application lifecycle unless an operator owns backup, upgrades, and recovery.

Release images use the commit SHA. The release workflow builds on pull requests
without pushing and publishes only on protected release events. Azure
authentication uses GitHub OIDC and a protected environment—there is no
long-lived Azure credential in GitHub.

Rollback is a correctness operation, not an admission of failure. Stop a bad
rollout, preserve telemetry, roll back the workload, verify probes and business
traffic, and only then investigate the failed revision.

## Part10 — Aspire

`Part10_Aspire` is a real Aspire 13.4.6 AppHost. It models PostgreSQL databases,
RabbitMQ, Redis, all four W7 roles, both W8 observability services, and the
deployment probe app. References and `WaitFor` edges express startup and
connection dependencies; secret parameters supply database, broker, and
gateway credentials.

```bash
aspire start --apphost src/Part10_Aspire
aspire publish --apphost src/Part10_Aspire --output-path artifacts/aspire
```

Aspire is the development application model and inner-loop orchestrator. It
also declares a Docker Compose publishing environment, so `aspire publish`
produces a Compose model and an environment-variable template instead of an
empty pipeline. Supply immutable image names, ports, and secret values in the
generated `.env` before deployment. It does not
replace YARP, gRPC contracts, Outbox/Inbox/Saga guarantees, Keycloak,
Kubernetes/ACA, or production secret and observability platforms. Published
artifacts must still pass the same image, security, probe, rollout, and
recovery review as hand-authored deployment assets.

## Local acceptance

Run generic .NET checks on any supported OS. Run Docker, PostgreSQL,
RabbitMQ, OTLP, diagnostics, container, and Aspire checks in Debian WSL:

```bash
dotnet test LearnAspNet.slnx -c Release --no-build \
  --max-parallel-test-modules 1
bash scripts/diagnostics/run-w8-observability-smoke.sh
bash scripts/diagnostics/run-w8-database-labs.sh
bash scripts/diagnostics/run-w8-diagnostics-smoke.sh
bash scripts/diagnostics/run-w8-k6-smoke.sh
bash scripts/deployment/run-container-smoke.sh
bash scripts/aspire/run-aspire-smoke.sh
aspire publish --apphost src/Part10_Aspire \
  --output-path artifacts/aspire-publish
```
