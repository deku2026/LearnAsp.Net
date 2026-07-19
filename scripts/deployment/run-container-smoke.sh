#!/usr/bin/env bash
set -euo pipefail

deployment_container="campus-w8-deployment-test"
troubleshooting_container="campus-w8-troubleshooting-test"
telemetry_container="campus-w8-telemetry-test"

cleanup() {
  docker rm --force \
    "$deployment_container" \
    "$troubleshooting_container" \
    "$telemetry_container" \
    >/dev/null 2>&1 || true
}
trap cleanup EXIT
cleanup

wait_healthy() {
  local container="$1"
  for _ in $(seq 1 60); do
    local status
    status="$(docker inspect --format "{{.State.Health.Status}}" "$container")"
    if [[ "$status" == "healthy" ]]; then
      return
    fi
    if [[ "$status" == "unhealthy" ]]; then
      docker logs "$container"
      return 1
    fi
    sleep 1
  done
  docker logs "$container"
  return 1
}

docker run --detach --rm \
  --name "$deployment_container" \
  --publish 7091:8080 \
  campus/deployment:local \
  Part09_Deployment.dll \
  >/dev/null
wait_healthy "$deployment_container"
curl --fail --silent http://127.0.0.1:7091/health/ready >/dev/null

docker run --detach --rm \
  --name "$troubleshooting_container" \
  --network dotnet-stack_dotnet-net \
  --env ASPNETCORE_ENVIRONMENT=Production \
  --env OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889 \
  --env OTEL_EXPORTER_OTLP_PROTOCOL=grpc \
  --env "ConnectionStrings__Troubleshooting=Host=postgres;Port=5432;Database=campus_w8_troubleshooting;Username=dotnet;Password=dotnet_dev" \
  campus/troubleshooting:local \
  Part08_2_TroubleshootingProcess.dll \
  >/dev/null
wait_healthy "$troubleshooting_container"

docker run --detach --rm \
  --name "$telemetry_container" \
  --network dotnet-stack_dotnet-net \
  --publish 7082:8080 \
  --env ASPNETCORE_ENVIRONMENT=Production \
  --env OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889 \
  --env OTEL_EXPORTER_OTLP_PROTOCOL=grpc \
  --env "Troubleshooting__BaseUrl=http://${troubleshooting_container}:8080" \
  campus/telemetry:local \
  Part08_1_OpenTelemetry.dll \
  >/dev/null
wait_healthy "$telemetry_container"

work_id="018f5f93-87c4-7f21-8e26-2c5daae92f91"
response="$(curl --fail --silent \
  "http://127.0.0.1:7082/api/observability/work/${work_id}?delayMs=50")"
grep --quiet "$work_id" <<<"$response"

for container in \
  "$deployment_container" \
  "$troubleshooting_container" \
  "$telemetry_container"; do
  docker inspect \
    --format "{{.Name}} user={{.Config.User}} health={{.State.Health.Status}}" \
    "$container"
done

echo "W8 non-root chiseled container smoke passed."
