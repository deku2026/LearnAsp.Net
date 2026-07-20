#!/usr/bin/env bash
set -euo pipefail
# Publishes the broken sample and asserts IL2026/IL3050 appear, then shows the
# fix order. NOT part of CI (the sample is out-of-solution).
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
sample="$repo_root/samples/Part11_2.TrimWarningLab"
echo "Publishing broken sample (expecting IL2026/IL3050)..."
log="$(mktemp)"
trap 'rm -f "$log"' EXIT
if dotnet publish "$sample" -c Release -r linux-x64 -o /tmp/aot-broken /p:PublishAot=true 2>"$log"; then
  echo "ERROR: broken sample published with NO warnings - expected IL2026/IL3050." >&2
  exit 1
fi
if ! grep -E 'IL2026|IL3050' "$log" >/dev/null; then
  echo "ERROR: publish failed but no IL2026/IL3050 in log." >&2
  cat "$log"
  exit 1
fi
echo "OK: broken sample produced expected trim/AOT warnings."
cat <<EOF
Fix order (from the spec):
  1. Eliminate reflection (preferred) - see samples/.../Fixed/Program.cs
  2. [DynamicallyAccessedMembers] on parameters/returns when the type is known
  3. [RequiresUnreferencedCode] to propagate the warning to callers
  4. [UnconditionalSuppressMessage] ONLY when proven safe, minimal scope
EOF
