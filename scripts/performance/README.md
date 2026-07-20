# W9 performance scripts

Run in Debian WSL after building Part11_1 in Release:

```
dotnet build src/Part11_1_PerformanceAdvanced -c Release
bash scripts/performance/run-threadpool-starvation-lab.sh
bash scripts/performance/run-gc-modes-lab.sh
```

Both scripts:

- check prerequisites (dotnet-counters, dotnet-stack, the built DLL);
- start the app with `Performance__FaultInjectionEnabled=true` and a lab token;
- wait for `/health/ready`, never fixed sleeps;
- run k6 via `docker run --rm --network host grafana/k6:2.0.0`;
- collect `dotnet-counters` (and `dotnet-stack` for the thread-pool lab);
- write a redacted `summary.md` under `artifacts/performance/.../`;
- delete sensitive full traces/stacks after extracting the fingerprint;
- record SDK, OS, CPU count, commit SHA in the summary.

These numbers are environment snapshots, not permanent promises. Re-run when
hardware, SDK, image, or load changes.
