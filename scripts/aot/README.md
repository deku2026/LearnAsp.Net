# W9 AOT scripts

Run in Debian WSL after installing the native AOT toolchain:

```
sudo apt-get install -y clang zlib1g-dev libssl-dev
bash scripts/aot/compare-publish-forms.sh
bash scripts/aot/publish-trim-warning-lab.sh
```

`compare-publish-forms.sh` publishes the Part11_2 app three ways
(self-contained `linux-x64` for JIT, ReadyToRun, Native AOT) and records publish
size, main binary size, SDK, OS, CPU, and commit. All three are self-contained
with the same RID so the size comparison is fair.

`publish-trim-warning-lab.sh` publishes the intentionally-broken
`samples/Part11_2.TrimWarningLab` sample and asserts that IL2026/IL3050 warnings
appear in the publish log. It then prints the four-step fix order. The sample is
NOT in the solution and does not run in CI.

## When to use each form

- **JIT self-contained**: long-running throughput services. Smallest publish
  for JIT, largest runtime footprint, warm JIT after steady state.
- **ReadyToRun (R2R)**: lower cold-start than JIT for a modest size increase.
  Lower migration cost than AOT.
- **Native AOT**: fastest cold start, smallest steady-state footprint, no JIT.
  Best for scale-to-zero, high instance density, or no-JIT environments. Not a
  universal replacement: reflection-heavy or dynamic-plugin workloads are
  poor fits, and EF Core AOT is still experimental.