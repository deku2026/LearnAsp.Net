#!/usr/bin/env bash
set -euo pipefail
# Fair three-form comparison: self-contained linux-x64 for JIT, R2R, Native AOT.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
proj="$repo_root/src/Part11_2_NativeAotTrim/Part11_2_NativeAotTrim.csproj"
out="$repo_root/artifacts/aot-comparison"
mkdir -p "$out"

if ! command -v clang >/dev/null 2>&1; then
  echo "clang is required for Native AOT. Install: sudo apt-get install -y clang zlib1g-dev libssl-dev" >&2
  exit 1
fi

publish() {
  local label="$1" extra="$2"
  local dir="$out/$label"
  rm -rf "$dir"
  dotnet publish "$proj" -c Release -r linux-x64 -o "$dir" $extra 2>"$out/${label}.log" || true
  local size
  size=$(du -sh "$dir" 2>/dev/null | cut -f1)
  local bin_size
  bin_size=$(du -sh "$dir/Part11_2_NativeAotTrim" 2>/dev/null | cut -f1 || echo "n/a")
  echo "$label: publish_dir=$size main_binary=$bin_size" | tee -a "$out/sizes.txt"
}

publish "jit-selfcontained" "/p:SelfContained=true"
publish "r2r-selfcontained" "/p:SelfContained=true /p:PublishReadyToRun=true"
publish "aot-selfcontained" "/p:PublishAot=true /p:SelfContained=true"

{
  echo "# W9 AOT three-form comparison"
  echo
  echo "Date: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "SDK: $(dotnet --version)"
  echo "OS: $(uname -srm)"
  echo "CPU: $(nproc)"
  echo "Commit: $(git -C "$repo_root" rev-parse --short HEAD 2>/dev/null || echo unknown)"
  echo
  echo "All three are self-contained linux-x64 (fair size comparison)."
  echo
  echo "## Sizes"
  cat "$out/sizes.txt"
} >"$out/summary.md"
echo "Summary: $out/summary.md"
