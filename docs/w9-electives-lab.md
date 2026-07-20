# W9 Part12 ElectiveBranches: Kafka replay + durable Mailpit delivery

Part12 adds the two elective branches the W9 plan selects, chosen because they
fill the remaining Docker matrix (Kafka, Mailpit) and exercise real messaging
semantics, not because they are the only valid directions.

## Kafka event stream + replay

Topic: `campus.enrollment.activity.v1` (+ `campus.enrollment.activity.dlq.v1`
for poison events). The producer uses `acks=all` and `EnableIdempotence=true`.
The consumer group commits manually — offset is committed only after the
business side effect (the idempotent inbox) records the enrollment id. If the
consumer crashes before commit, Kafka re-delivers; the inbox dedup makes the
second delivery a no-op.

### What Kafka does NOT guarantee

- **Partition-only ordering.** Same key (`enrollmentId`) guarantees same
  partition and therefore in-order delivery for that key. Different keys can
  arrive in any order across partitions.
- **Idempotent producer ≠ business exactly-once.** The idempotent producer
  prevents duplicate *broker writes*; the consumer's business side effect still
  needs idempotency (the inbox) because at-least-once delivery means a message
  can be delivered more than once after a crash before commit.
- **Rebalance changes the picture.** During a rebalance, partitions move
  between consumers. The poll loop must not block; commit and cancellation must
  be correct or messages can be lost or double-processed.

### Replay vs RabbitMQ work queues

A second consumer group reading from the head proves group independence
(each group maintains its own offset). Seek/reset to an earlier offset replays
messages — this is the key semantic difference from RabbitMQ work queues,
where a consumed message is gone. Kafka's retention window defines how far
back you can replay.

## Durable background email + Mailpit

The email scheduler uses a PostgreSQL job store with `FOR UPDATE SKIP LOCKED`
to acquire the next job — this is the same pattern W7's Outbox relay uses, and
it prevents two scheduler instances from picking up the same job. The job
table is in the isolated `campus_w9_notifications` database (per-wave DB
pattern: `campus_w7_*`, `campus_w8_*`, now `campus_w9_notifications`).

- `POST /api/notifications/email` accepts a course notice, returns 202 + job
  location. `Idempotency-Key` header prevents duplicate scheduling (UNIQUE
  constraint on `idempotency_key`).
- `GET /api/notifications/jobs/{id}` returns job state.
- `DELETE /api/notifications/jobs/{id}` cancels if not yet started.
- The scheduler polls every `PollIntervalMs`, acquires with `FOR UPDATE SKIP
  LOCKED`, sends via Mailpit SMTP `localhost:1125`, marks completed on success,
  marks failed with exponential backoff on failure (`min(base * 2^attempt,
  maxBackoff)`), and stops after `MaxAttempts`.
- Mailpit HTTP API `localhost:8025` is used for assertions (recipient, subject,
  HTML/text, correlation header). Random `Date`/`Message-ID` are not asserted
  by exact value.
- All recipients use `@example.test` — never a real address. Logs record job
  id, attempt, and provider message id, never the body or sensitive address.

## What the tests prove

- **Generic (cross-platform):** address validation rejects non-`example.test`
  with 400; backoff schedule math is bounded; Kafka status endpoint returns the
  configured group and topic. These run without Docker.
- **Docker (Linux/WSL only):** real Kafka produce/consume with inbox dedup;
  re-publish of the same enrollment id is a no-op; Mailpit SMTP delivery with
  HTTP API assertion of subject and recipient; idempotency-key prevents
  duplicate job scheduling. These use `Assert.SkipWhen` when Docker/infra is
  unavailable.

## Why not the other directions

SignalR/SSE (Step05 has SSE), Orleans (no海量 actor problem), Dapr (sidecar
adds ops), GraphQL (no multi-end aggregation need), AI/RAG (adds model/vector
governance). Skipping these is a scope choice, not a capability judgment.