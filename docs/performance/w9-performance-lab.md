# W9 Part11_1 performance lab

The performance lab produces measured evidence, not faster-looking APIs. It
targets four questions the closing wave must answer:

1. **Why does the system get slow under load?** The thread-pool starvation
   fingerprint is rising `dotnet.thread_pool.queue.length`, slowly growing
   `dotnet.thread_pool.thread.count`, CPU not saturated, and blocked stacks
   dominated by sync-over-async work. The `/lab/threadpool/blocking` endpoint
   reproduces it; `/lab/threadpool/async` is the same business semantics with
   `await`. The script
   `scripts/performance/run-threadpool-starvation-lab.sh` runs k6 against both
   while collecting `dotnet-counters` and `dotnet-stack`, and emits a
   before/after summary. The conclusion is throughput + queue delta, never the
   average latency alone.

2. **What does GC mode cost?** GC mode is process-start config, not
   hot-swappable. `scripts/performance/run-gc-modes-lab.sh` starts three
   separate processes (Workstation, Server + DATAS, Server + no DATAS) with
   identical CPU, payload, and duration, and records working set, heap size,
   Gen0/1/2 counts, pause time, P95/P99, and throughput. The trade-off is
   footprint vs throughput, not "one is universally better."

3. **Is source-generated JSON actually cheaper?** `BenchmarkDotNet` in
   `benchmarks/Part11_1_PerformanceBenchmarks` compares reflection metadata
   path vs `JsonSerializerContext` source generation. `MemoryDiagnoser`
   captures Mean, Allocated, Gen0/1/2. The benchmark project builds in CI but
   never runs under `dotnet test` — micro-benchmarks measure CPU/memory hot
   paths, never I/O.

4. **When is Span/ArrayPool worth it?** `CourseCodeParsingBenchmarks`
   compares `string.Split` baseline vs a `Span<byte>`/ArrayPool implementation.
   Both must return identical results (the tests prove it). Span/Pool are
   introduced only after the benchmark shows the allocation is real — never
   preemptively.

## Anti-patterns this lab exists to reject

- **Averages as performance.** P95/P99, error rate, throughput, and saturation
  are the signal. Mean hides the tail.
- **BenchmarkDotNet doing I/O.** Micro-benchmarks are CPU/memory hot paths
  only. No network, database, or disk in a benchmark.
- **Narrow thresholds on shared runners.** `deploy/k6/w9-performance.js` uses
  generous PR-smoke thresholds (P95 < 500ms, P99 < 1000ms, error rate < 1%).
  Strict P95/P99/throughput belongs on a fixed/self-hosted runner, not a
  shared GitHub runner where jitter causes flaky failures.
- **Premature Span/Pool.** Clear baseline first; optimize with evidence.

## Evidence recording

Every script records SDK, OS, CPU count, and commit SHA in its summary. The
numbers are an environment snapshot, not a permanent promise. Re-run when
hardware, SDK, image, or load changes.
