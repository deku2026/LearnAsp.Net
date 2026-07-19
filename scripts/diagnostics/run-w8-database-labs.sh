#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
app_pid=""

cleanup() {
  if [[ -n "$app_pid" ]]; then
    kill "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

export ConnectionStrings__Troubleshooting="${CAMPUS_W8_PG:-Host=localhost;Port=5432;Database=campus_w8_troubleshooting;Username=dotnet;Password=dotnet_dev;Maximum Pool Size=20}"
export Troubleshooting__FaultInjectionEnabled=true
export Troubleshooting__LabToken="${CAMPUS_W8_LAB_TOKEN:-w8-real-lab-token}"
export OTEL_EXPORTER_OTLP_ENDPOINT="${OTEL_EXPORTER_OTLP_ENDPOINT:-http://localhost:4317}"

dotnet \
  "$repo_root/src/Part08_2_TroubleshootingProcess/bin/Debug/net10.0/Part08_2_TroubleshootingProcess.dll" \
  --urls http://127.0.0.1:6082 \
  >/tmp/campus-w8-database-labs.log 2>&1 &
app_pid="$!"

for _ in $(seq 1 60); do
  if curl --fail --silent http://127.0.0.1:6082/health/ready >/dev/null; then
    break
  fi
  sleep 0.25
done
curl --fail --silent http://127.0.0.1:6082/health/ready >/dev/null

status="$(curl --silent --output /dev/null --write-out "%{http_code}" \
  http://127.0.0.1:6082/lab/database?delayMs=10)"
[[ "$status" == "401" ]]

curl --fail --silent \
  --header "X-Lab-Token: ${Troubleshooting__LabToken}" \
  "http://127.0.0.1:6082/lab/database?delayMs=50"

curl --fail --silent \
  --header "X-Lab-Token: ${Troubleshooting__LabToken}" \
  "http://127.0.0.1:6082/lab/connection-pool?connections=4&holdMs=50"

echo "W8 real PostgreSQL fault labs passed."
