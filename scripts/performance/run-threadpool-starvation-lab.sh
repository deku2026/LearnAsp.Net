#!/usr/bin/env bash
set -euo pipefail

# W9 Part11_1 thread-pool starvation lab.
# Runs in Debian WSL against the Part11_1 app. Produces a redacted before/after
# summary under artifacts/performance/threadpool/. Deletes sensitive dotnet-stack
# traces after extracting the high-level fingerprint.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts="$repo_root/artifacts/performance/threadpool"
mkdir -p "$artifacts"
lab_token="${LAB_TOKEN:-lab-token}"
summary="$artifacts/summary.md"

if ! command -v dotnet-counters >/dev/null 2>&1; then
  echo "dotnet-counters is required (dotnet tool install -g dotnet-counters)" >&2
  exit 1
fi
if ! command -v dotnet-stack >/dev/null 2>&1; then
  echo "dotnet-stack is required" >&2
  exit 1
fi

app_log="$(mktemp)"
blocking_counters="$(mktemp)"
async_counters="$(mktemp)"
stacks_file="$(mktemp)"
pid=""
cleanup() {
  [[ -n "$pid" ]] && kill "$pid" 2>/dev/null || true
  [[ -n "$pid" ]] && wait "$pid" 2>/dev/null || true
  cp "$app_log" "$artifacts/app.log" 2>/dev/null || true
  rm -f "$app_log" "$stacks_file"
}
trap cleanup EXIT

ASPNETCORE_URLS=http://127.0.0.1:5027 \
Performance__FaultInjectionEnabled=true \
Performance__LabToken="$lab_token" \
OTEL_EXPORTER_OTLP_ENDPOINT= \
  dotnet "$repo_root/src/Part11_1_PerformanceAdvanced/bin/Release/net10.0/Part11_1_PerformanceAdvanced.dll" \
  >"$app_log" 2>&1 &
pid=$!

for _ in {1..30}; do
  if curl --fail --silent http://127.0.0.1:5027/health/ready >/dev/null; then
    break
  fi
  sleep 1
done
curl --fail --silent http://127.0.0.1:5027/health/ready >/dev/null

record_session() {
  local label="$1" duration="${2:-60}" counters_file="$3"
  dotnet-counters collect --process-id "$pid" \
    --duration "00:00:${duration}" --format csv --output "$counters_file" &
  local counters_pid=$!
  docker run --rm --network host \
    -e BASE_URL=http://127.0.0.1:5027 \
    -e LAB_TOKEN="$lab_token" \
    -e DURATION="${duration}s" \
    -v "$repo_root/deploy/k6:/scripts:ro" \
    grafana/k6:2.0.0 run /scripts/w9-performance.js >"$artifacts/${label}-k6.json" 2>"$artifacts/${label}-k6.log" || true
  wait "$counters_pid" || true
}

record_session "blocking" 60 "$blocking_counters"
dotnet-stack report --process-id "$pid" >"$stacks_file" 2>/dev/null || true
record_session "async" 60 "$async_counters"

{
  echo "# W9 thread-pool starvation lab"
  echo
  echo "Date: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "SDK: $(dotnet --version)"
  echo "OS: $(uname -srm)"
  echo "CPU: $(nproc)"
  echo "Commit: $(git -C "$repo_root" rev-parse --short HEAD 2>/dev/null || echo unknown)"
  echo
  echo "## Blocking (/lab/threadpool/blocking)"
  grep -E 'thread_pool.thread.count|thread_pool.queue.length|thread_pool.work_item.count' \
    "$blocking_counters" | tail -20 || echo "(no counter rows)"
  echo
  echo "## Async (/lab/threadpool/async)"
  grep -E 'thread_pool.thread.count|thread_pool.queue.length|thread_pool.work_item.count' \
    "$async_counters" | tail -20 || echo "(no counter rows)"
  echo
  echo "k6 JSON evidence is in artifacts/performance/threadpool/*.json"
} >"$summary"

echo "Summary: $summary"
