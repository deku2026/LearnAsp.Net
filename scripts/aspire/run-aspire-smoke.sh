#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
apphost="$repo_root/src/Part10_Aspire/Part10_Aspire.csproj"
artifacts="$repo_root/artifacts/aspire-smoke"
mkdir -p "$artifacts"
started=false

cleanup() {
  rm -f \
    "$artifacts/start.raw.json" \
    "$artifacts/resources.raw.json"
  if [[ "$started" == "true" ]]; then
    aspire stop \
      --apphost "$apphost" \
      --non-interactive \
      >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

env \
  "Parameters__postgres-password=${CAMPUS_ASPIRE_POSTGRES_PASSWORD:-aspire-postgres-local}" \
  "Parameters__rabbit-password=${CAMPUS_ASPIRE_RABBIT_PASSWORD:-aspire-rabbit-local}" \
  "Parameters__gateway-signing-key=${CAMPUS_ASPIRE_SIGNING_KEY:-aspire-signing-key-at-least-32-bytes-local}" \
  "Parameters__gateway-internal-token=${CAMPUS_ASPIRE_INTERNAL_TOKEN:-aspire-internal-token-at-least-32-bytes-local}" \
  aspire start \
    --apphost "$apphost" \
    --non-interactive \
    --format Json \
    >"$artifacts/start.raw.json"
started=true

python3 - "$artifacts/start.raw.json" "$artifacts/start.json" <<'PY'
import json
import sys
import urllib.parse

with open(sys.argv[1], encoding="utf-8") as stream:
    started = json.load(stream)

dashboard = urllib.parse.urlsplit(started["dashboardUrl"])
summary = {
    "appHostPath": started["appHostPath"],
    "dashboardUrl": urllib.parse.urlunsplit(
        (dashboard.scheme, dashboard.netloc, dashboard.path, "", "")
    ),
}
with open(sys.argv[2], "w", encoding="utf-8") as stream:
    json.dump(summary, stream, indent=2)
    stream.write("\n")
PY
rm -f "$artifacts/start.raw.json"

for resource in postgres rabbitmq redis catalog notices enrollment gateway troubleshooting telemetry-lab deployment-lab; do
  if ! aspire wait "$resource" \
      --apphost "$apphost" \
      --status healthy \
      --timeout 180 \
      --non-interactive; then
    aspire logs "$resource" \
      --apphost "$apphost" \
      --tail 200 \
      --timestamps \
      --non-interactive || true
    exit 1
  fi
done

aspire describe \
  --apphost "$apphost" \
  --format Json \
  --non-interactive \
  >"$artifacts/resources.raw.json"

python3 - "$artifacts/resources.raw.json" "$artifacts/resources.json" <<'PY'
import json
import sys
import urllib.request
import uuid

required = {
    "postgres",
    "rabbitmq",
    "redis",
    "catalog",
    "notices",
    "enrollment",
    "gateway",
    "troubleshooting",
    "telemetry-lab",
    "deployment-lab",
}
with open(sys.argv[1], encoding="utf-8") as stream:
    resources = json.load(stream)["resources"]

by_name = {resource["displayName"]: resource for resource in resources}
missing = required.difference(by_name)
if missing:
    raise SystemExit(f"Missing Aspire resources: {sorted(missing)}")

not_running = {
    name: by_name[name].get("state")
    for name in required
    if by_name[name].get("state") != "Running"
}
if not_running:
    raise SystemExit(f"Aspire resources not running: {not_running}")

unhealthy = {
    name: by_name[name].get("healthStatus")
    for name in required
    if by_name[name].get("healthStatus") != "Healthy"
}
if unhealthy:
    raise SystemExit(f"Aspire resources not healthy: {unhealthy}")

telemetry_url = next(
    endpoint["url"]
    for endpoint in by_name["telemetry-lab"]["urls"]
    if endpoint["name"] == "http"
)
work_id = str(uuid.uuid4())
with urllib.request.urlopen(
    f"{telemetry_url}/api/observability/work/{work_id}?delayMs=25",
    timeout=10,
) as response:
    workload = json.load(response)

if workload.get("workId") != work_id:
    raise SystemExit("Aspire cross-service workload returned the wrong work id")
if len(workload.get("traceId", "")) != 32 or "result" not in workload:
    raise SystemExit("Aspire cross-service workload did not return trace context")

summary = {
    "resources": [
        {
            "displayName": name,
            "state": by_name[name]["state"],
            "healthStatus": by_name[name]["healthStatus"],
            "urls": by_name[name]["urls"],
        }
        for name in sorted(required)
    ],
    "crossServiceWorkload": {
        "workId": workload["workId"],
        "traceId": workload["traceId"],
        "spanId": workload["spanId"],
    },
}
with open(sys.argv[2], "w", encoding="utf-8") as stream:
    json.dump(summary, stream, indent=2)
    stream.write("\n")
PY
rm -f "$artifacts/resources.raw.json"

echo "W8 Aspire application model smoke passed."
