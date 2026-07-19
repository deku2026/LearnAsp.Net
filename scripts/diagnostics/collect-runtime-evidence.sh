#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "Usage: $0 <pid> <output-directory> [--with-dump]" >&2
  exit 2
fi

pid="$1"
output_directory="$2"
with_dump="${3:-}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
target="${output_directory%/}/${timestamp}-pid-${pid}"
mkdir -p "$target"

if ! kill -0 "$pid" 2>/dev/null; then
  echo "Process $pid is not running or is not accessible." >&2
  exit 1
fi

dotnet-counters collect \
  --process-id "$pid" \
  --duration 00:00:10 \
  --format csv \
  --output "$target/counters.csv"

dotnet-trace collect \
  --process-id "$pid" \
  --duration 00:00:10 \
  --output "$target/runtime.nettrace"

dotnet-stack report \
  --process-id "$pid" \
  >"$target/stacks.txt"

dotnet-gcdump collect \
  --process-id "$pid" \
  --output "$target/heap.gcdump"

if [[ "$with_dump" == "--with-dump" ]]; then
  dotnet-dump collect \
    --process-id "$pid" \
    --type Heap \
    --output "$target/process.dmp"
fi

cat >"$target/README.txt" <<EOF
UTC collection time: $timestamp
Process ID: $pid

Treat all diagnostic artifacts as sensitive. Dumps, traces, stacks, and GC
dumps can contain request data, secrets, or personal data. Do not commit them.
Retain and share them only under the incident data-handling policy.
EOF

echo "$target"
