#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
evidence="$(mktemp -d)"
service_log="$(mktemp)"
lab_token="w8-diagnostics-local-only"

cleanup() {
  kill "${service_pid:-}" 2>/dev/null || true
  wait "${service_pid:-}" 2>/dev/null || true
  rm -rf "$evidence"
  rm -f "$service_log"
}
trap cleanup EXIT

Troubleshooting__FaultInjectionEnabled=true \
  Troubleshooting__LabToken="$lab_token" \
  ASPNETCORE_URLS=http://127.0.0.1:6082 \
  dotnet "$repo_root/src/Part08_2_TroubleshootingProcess/bin/Release/net10.0/Part08_2_TroubleshootingProcess.dll" \
  >"$service_log" 2>&1 &
service_pid=$!

for _ in {1..30}; do
  if curl --fail --silent http://127.0.0.1:6082/health/live >/dev/null; then
    break
  fi
  sleep 1
done
curl --fail --silent http://127.0.0.1:6082/health/live >/dev/null

curl --fail --silent \
  -X POST \
  -H "X-Lab-Token: $lab_token" \
  "http://127.0.0.1:6082/lab/memory?megabytes=32" \
  >/dev/null

target="$(
  "$repo_root/scripts/diagnostics/collect-runtime-evidence.sh" \
    "$service_pid" \
    "$evidence" \
    --with-dump |
    tail -n 1
)"

for artifact in counters.csv runtime.nettrace stacks.txt heap.gcdump process.dmp README.txt; do
  if [[ ! -s "$target/$artifact" ]]; then
    echo "Missing or empty diagnostic artifact: $artifact" >&2
    exit 1
  fi
done

echo "W8 counters, trace, stack, GC dump, and process dump smoke passed."
