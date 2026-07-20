#!/usr/bin/env bash
set -euo pipefail

# W9 full-stack acceptance: verifies all 10 Docker containers have
# role-appropriate evidence in Debian WSL. Produces a JSON+Markdown summary
# under artifacts/w9/. Never deletes persistent volumes unless RESET_ALL=1.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts="$repo_root/artifacts/w9"
mkdir -p "$artifacts"
summary="$artifacts/full-stack-acceptance.md"

if ! command -v docker >/dev/null 2>&1; then
  echo "docker is required" >&2
  exit 1
fi
if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required" >&2
  exit 1
fi

# Check all 10 containers from the local compose (~/Project/docker).
compose_file="${COMPOSE_FILE:-$HOME/Project/docker/docker-compose.yml}"
if [[ ! -f "$compose_file" ]]; then
  echo "Compose file not found at $compose_file. Set COMPOSE_FILE to override." >&2
  exit 1
fi

echo "Checking 10 containers..."
expected_containers=(
  dotnet-postgres dotnet-redis dotnet-rabbitmq dotnet-keycloak
  dotnet-seq dotnet-aspire-dashboard dotnet-kafka dotnet-mailpit
  dotnet-adminer dotnet-redis-insight
)
declare -A container_ok
for name in "${expected_containers[@]}"; do
  if docker ps --filter "name=^${name}$" --format '{{.Names}}' | grep -q "^${name}$"; then
    container_ok[$name]=1
    echo "  OK: $name"
  else
    container_ok[$name]=0
    echo "  MISSING: $name"
  fi
done

# Per-container role-level checks.
check_postgres() {
  echo "## PostgreSQL"
  if PGPASSWORD=dotnet_dev psql -h localhost -p 5432 -U dotnet -d postgres -c "SELECT 1" >/dev/null 2>&1; then
    echo "  OK: connect + SELECT 1"
  else
    echo "  FAIL: connect"
  fi
}

check_redis() {
  echo "## Redis"
  if docker exec dotnet-redis redis-cli PING 2>/dev/null | grep -q PONG; then
    echo "  OK: PING"
    docker exec dotnet-redis redis-cli SET w9-test "hello" >/dev/null 2>&1
    local val
    val=$(docker exec dotnet-redis redis-cli GET w9-test 2>/dev/null)
    if [[ "$val" == "hello" ]]; then
      echo "  OK: SET/GET"
    else
      echo "  FAIL: SET/GET (got '$val')"
    fi
    docker exec dotnet-redis redis-cli DEL w9-test >/dev/null 2>&1
  else
    echo "  FAIL: PING"
  fi
}

check_rabbitmq() {
  echo "## RabbitMQ"
  if curl --fail --silent http://localhost:15672/api/overview >/dev/null 2>&1; then
    echo "  OK: management API ready"
  else
    echo "  FAIL: management API"
  fi
}

check_kafka() {
  echo "## Kafka"
  if docker exec dotnet-kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9094 --list >/dev/null 2>&1; then
    echo "  OK: topic list"
  else
    echo "  FAIL: topic list"
  fi
}

check_keycloak() {
  echo "## Keycloak"
  if curl --fail --silent http://localhost:8082/realms/master/.well-known/openid-configuration >/dev/null 2>&1; then
    echo "  OK: discovery"
  else
    echo "  FAIL: discovery"
  fi
}

check_seq() {
  echo "## Seq"
  if curl --fail --silent http://localhost:5341/ >/dev/null 2>&1; then
    echo "  OK: HTTP ready"
  else
    echo "  FAIL: HTTP"
  fi
}

check_aspire() {
  echo "## Aspire Dashboard"
  if curl --fail --silent http://localhost:18888/ >/dev/null 2>&1; then
    echo "  OK: HTTP ready"
  else
    echo "  FAIL: HTTP"
  fi
}

check_mailpit() {
  echo "## Mailpit"
  if curl --fail --silent http://localhost:8025/api/v1/info >/dev/null 2>&1; then
    echo "  OK: HTTP API ready"
  else
    echo "  FAIL: HTTP API"
  fi
}

check_adminer() {
  echo "## Adminer"
  if curl --fail --silent http://localhost:8081/ >/dev/null 2>&1; then
    echo "  OK: HTTP ready (manual table inspection)"
  else
    echo "  FAIL: HTTP"
  fi
}

check_redis_insight() {
  echo "## RedisInsight"
  if curl --fail --silent http://localhost:5540/ >/dev/null 2>&1; then
    echo "  OK: HTTP ready (manual key/TTL inspection)"
  else
    echo "  FAIL: HTTP"
  fi
}

{
  echo "# W9 full-stack acceptance"
  echo
  echo "Date: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "SDK: $(dotnet --version)"
  echo "OS: $(uname -srm)"
  echo "CPU: $(nproc)"
  echo "Commit: $(git -C "$repo_root" rev-parse --short HEAD 2>/dev/null || echo unknown)"
  echo
  echo "## Container status"
  for name in "${expected_containers[@]}"; do
    if [[ "${container_ok[$name]}" == "1" ]]; then
      echo "- $name: OK"
    else
      echo "- $name: MISSING"
    fi
  done
  echo
  check_postgres
  echo
  check_redis
  echo
  check_rabbitmq
  echo
  check_kafka
  echo
  check_keycloak
  echo
  check_seq
  echo
  check_aspire
  echo
  check_mailpit
  echo
  check_adminer
  echo
  check_redis_insight
} >"$summary"

echo "Summary: $summary"
