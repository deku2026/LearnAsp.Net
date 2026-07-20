#!/usr/bin/env bash
set -euo pipefail

# W9 Part11_1 GC modes comparison.
# Starts THREE separate processes (GC mode is process-start config, not
# hot-swappable):
#   1. Workstation GC
#   2. Server GC + DATAS (default)
#   3. Server GC + DOTNET_GCDynamicAdaptationMode=0
# Same CPU, payload, duration. Records working set, heap, allocation rate,
# Gen0/1/2, pause, P95/P99, throughput. Multi-run, median + dispersion.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts="$repo_root/artifacts/performance/gc"
mkdir -p "$artifacts"
lab_token="${LAB_TOKEN:-lab-token}"
app="$repo_root/src/Part11_1_PerformanceAdvanced/bin/Release/net10.0/Part11_1_PerformanceAdvanced.dll"

if [[ ! -f "$app" ]]; then
  echo "Build Part11_1 first: dotnet build src/Part11_1_PerformanceAdvanced -c Release" >&2
  exit 1
fi

run_mode() {
  local label="$1" gc_env="$2" datas_env="$3"
  local out="$artifacts/$label"
  mkdir -p "$out"
  local pid=""
  local counters
  counters="$(mktemp)"
  cleanup() { [[ -n "$pid" ]] && kill "$pid" 2>/dev/null || true; }
  trap cleanup RETURN
  DOTNET_GCServer="$gc_env" DOTNET_GCDynamicAdaptationMode="$datas_env" \
    ASPNETCORE_URLS=http://127.0.0.1:5027 \
    Performance__FaultInjectionEnabled=true \
    Performance__LabToken="$lab_token" \
    OTEL_EXPORTER_OTLP_ENDPOINT= \
    dotnet "$app" >"$out/app.log" 2>&1 &
  pid=$!
  for _ in {1..30}; do
    curl --fail --silent http://127.0.0.1:5027/health/ready >/dev/null 2>&1 && break
    sleep 1
  done
  dotnet-counters collect --process-id "$pid" \
    --duration 00:00:30 --format csv --output "$counters" || true
  docker run --rm --network host \
    -e BASE_URL=http://127.0.0.1:5027 \
    -e LAB_TOKEN="$lab_token" -e DURATION=25s \
    -v "$repo_root/deploy/k6:/scripts:ro" \
    grafana/k6:2.0.0 run /scripts/w9-performance.js >"$out/k6.json" 2>"$out/k6.log" || true
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  cp "$counters" "$out/counters.csv"
  rm -f "$counters"
  echo "$label done"
}

run_mode "workstation" "0" "1"
run_mode "server-datas" "1" "1"
run_mode "server-nodatas" "1" "0"

{
  echo "# W9 GC modes lab"
  echo
  echo "Date: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "SDK: $(dotnet --version)"
  echo "OS: $(uname -srm)"
  echo "CPU: $(nproc)"
  echo "Commit: $(git -C "$repo_root" rev-parse --short HEAD 2>/dev/null || echo unknown)"
  echo
  for label in workstation server-datas server-nodatas; do
    echo "## $label"
    grep -E 'gc.heap.size|gen-0-gc|gen-1-gc|gen-2-gc|time-in-gc' \
      "$artifacts/$label/counters.csv" | tail -15 || echo "(no rows)"
    echo
  done
} >"$artifacts/summary.md"
echo "Summary: $artifacts/summary.md"
