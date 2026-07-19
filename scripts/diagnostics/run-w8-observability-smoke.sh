#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts="${repo_root}/artifacts/w8-smoke"
mkdir -p "$artifacts"

telemetry_pid=""
troubleshooting_pid=""

cleanup() {
  [[ -n "$telemetry_pid" ]] && kill "$telemetry_pid" 2>/dev/null || true
  [[ -n "$troubleshooting_pid" ]] && kill "$troubleshooting_pid" 2>/dev/null || true
  [[ -n "$telemetry_pid" ]] && wait "$telemetry_pid" 2>/dev/null || true
  [[ -n "$troubleshooting_pid" ]] && wait "$troubleshooting_pid" 2>/dev/null || true
}
trap cleanup EXIT

export ASPNETCORE_ENVIRONMENT=Development
export OTEL_EXPORTER_OTLP_ENDPOINT="${OTEL_EXPORTER_OTLP_ENDPOINT:-http://localhost:4317}"
export OTEL_EXPORTER_OTLP_PROTOCOL="${OTEL_EXPORTER_OTLP_PROTOCOL:-grpc}"
export ConnectionStrings__Troubleshooting="${CAMPUS_W8_PG:-Host=localhost;Port=5432;Database=campus_w8_troubleshooting;Username=dotnet;Password=dotnet_dev}"

dotnet \
  "$repo_root/src/Part08_2_TroubleshootingProcess/bin/Debug/net10.0/Part08_2_TroubleshootingProcess.dll" \
  --urls http://127.0.0.1:6082 \
  >"$artifacts/troubleshooting.log" 2>&1 &
troubleshooting_pid="$!"

Troubleshooting__BaseUrl=http://127.0.0.1:6082 \
dotnet \
  "$repo_root/src/Part08_1_OpenTelemetry/bin/Debug/net10.0/Part08_1_OpenTelemetry.dll" \
  --urls http://127.0.0.1:6081 \
  >"$artifacts/telemetry.log" 2>&1 &
telemetry_pid="$!"

sleep 0.25
kill -0 "$troubleshooting_pid"
kill -0 "$telemetry_pid"

for endpoint in \
  http://127.0.0.1:6082/health/live \
  http://127.0.0.1:6081/health/live; do
  for _ in $(seq 1 60); do
    if curl --fail --silent "$endpoint" >/dev/null; then
      break
    fi
    sleep 0.25
  done
  curl --fail --silent "$endpoint" >/dev/null
done

work_id="018f5f93-87c4-7f21-8e26-2c5daae92f90"
curl --fail --silent \
  "http://127.0.0.1:6081/api/observability/work/${work_id}?delayMs=75" \
  | tee "$artifacts/work-response.json"

dotnet-counters collect \
  --process-id "$troubleshooting_pid" \
  --duration 00:00:05 \
  --format csv \
  --output "$artifacts/troubleshooting-counters.csv"

dotnet-trace collect \
  --process-id "$troubleshooting_pid" \
  --duration 00:00:05 \
  --output "$artifacts/troubleshooting.nettrace"

dotnet-stack report \
  --process-id "$troubleshooting_pid" \
  >"$artifacts/troubleshooting-stacks.txt"

curl --fail --silent http://localhost:18888/ >/dev/null
echo "W8 observability smoke passed; inspect traces, metrics, and logs at http://localhost:18888"
