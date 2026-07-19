# Incident RCA

## Summary

- Incident ID:
- Start/end UTC:
- User-visible impact:
- Affected services, regions, tenants, and revisions:
- Detection source:

## Timeline

List evidence-backed events in UTC: deployment, alert, mitigation, recovery,
and validation. Distinguish observation time from event time.

## Six-step evidence

### 1. Metrics

Record rate, errors, latency percentiles, saturation, scope, and screenshots or
query links.

### 2. Traces

Record representative trace IDs, the first abnormal span, retries, and critical
path.

### 3. Logs

Record structured queries and event IDs. Redact personal data and credentials.

### 4. Database

Record waits, locks, pool state, long transactions, plans, and query count.

### 5. Runtime

Record counters, trace/stack/dump filenames, collection UTC, process/revision,
and protected evidence location.

### 6. Deployment

Record image digests, configuration differences, probe events, rollout state,
and rollback result.

## Root cause and contributing factors

State the causal mechanism. Separate root cause, trigger, amplifiers, and gaps
in detection or mitigation. Avoid naming a person as the cause.

## Recovery and validation

Document the mitigation, why it was safe, business-flow checks, telemetry
recovery, and the observation window.

## Corrective actions

Each action needs an owner, due date, verification method, and category:
prevention, detection, mitigation, or recovery. Link tests and runbooks.
