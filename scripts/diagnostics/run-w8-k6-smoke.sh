#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts="$repo_root/artifacts/k6"
mkdir -p "$artifacts"
telemetry_log="$(mktemp)"
troubleshooting_log="$(mktemp)"

cleanup() {
  kill "${telemetry_pid:-}" "${troubleshooting_pid:-}" 2>/dev/null || true
  wait "${telemetry_pid:-}" "${troubleshooting_pid:-}" 2>/dev/null || true
  cp "$telemetry_log" "$artifacts/telemetry.log"
  cp "$troubleshooting_log" "$artifacts/troubleshooting.log"
  rm -f "$telemetry_log" "$troubleshooting_log"
}
trap cleanup EXIT

ASPNETCORE_URLS=http://127.0.0.1:6082 \
  dotnet "$repo_root/src/Part08_2_TroubleshootingProcess/bin/Release/net10.0/Part08_2_TroubleshootingProcess.dll" \
  >"$troubleshooting_log" 2>&1 &
troubleshooting_pid=$!

Troubleshooting__BaseUrl=http://127.0.0.1:6082 \
  ASPNETCORE_URLS=http://127.0.0.1:6081 \
  dotnet "$repo_root/src/Part08_1_OpenTelemetry/bin/Release/net10.0/Part08_1_OpenTelemetry.dll" \
  >"$telemetry_log" 2>&1 &
telemetry_pid=$!

for _ in {1..30}; do
  if curl --fail --silent http://127.0.0.1:6081/health/ready >/dev/null &&
      curl --fail --silent http://127.0.0.1:6082/health/ready >/dev/null; then
    break
  fi
  sleep 1
done

curl --fail --silent http://127.0.0.1:6081/health/ready >/dev/null
curl --fail --silent http://127.0.0.1:6082/health/ready >/dev/null

for _ in {1..25}; do
  curl --fail --silent \
    "http://127.0.0.1:6081/api/observability/work/$(< /proc/sys/kernel/random/uuid)?delayMs=25" \
    >/dev/null
done

docker run --rm \
  --network host \
  -e BASE_URL=http://127.0.0.1:6081 \
  -v "$repo_root/deploy/k6:/scripts:ro" \
  grafana/k6:2.0.0 \
  run /scripts/w8-observability.js
