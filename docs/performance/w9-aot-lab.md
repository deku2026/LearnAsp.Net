# W9 Part11_2 Native AOT and Trim lab

The AOT lab is a deliberately constrained Campus Minimal API that proves three
deployment trade-offs, not a claim that "AOT is universally better."

## Three deployment forms (fair comparison)

`scripts/aot/compare-publish-forms.sh` publishes the same app three ways, all
self-contained `linux-x64` so the size comparison is fair:

| Form | Cold start | Steady-state footprint | Publish size | Best for |
| --- | --- | --- | --- | --- |
| JIT self-contained | Warm-up cost | Larger (JIT + runtime) | Moderate | Long-running throughput services |
| ReadyToRun (R2R) | Lower than JIT | Larger than JIT | Larger than JIT | Cheaper startup optimization than AOT |
| Native AOT | Fastest | Smallest (no JIT) | Smallest binary, self-contained deps | Cold start, scale-to-zero, high density, no-JIT |

The conclusion is a trade-off, not a ranking. Long-running throughput services
usually prefer JIT; R2R is a lower-cost startup optimization; AOT fits cold
start, scale-to-zero, high instance density, or no-JIT environments.

## Why STJ source generation and RDG are the AOT foundation

`AotJsonContext` (STJ `JsonSerializerContext`) gives the serializer compile-time
type metadata, eliminating the reflection-based metadata path that trim/AOT
cannot keep. The Request Delegate Generator (RDG) does the same for endpoint
delegates: it emits statically-discovered request handling code so the runtime
does not reflect over lambda parameters.

The referenced-library pitfall: `Part11_2_NativeAotTrim.Endpoints` is a class
library that the AOT app references. RDG must be explicitly enabled in the
*referenced library* (`EnableRequestDelegateGenerator=true`) — the main app
setting `PublishAot` does not automatically opt a referenced library into RDG.
This is pitfall #12 from the plan. The compile-evidence test
`EndpointsLibraryCsprojDeclaresRdgAndAotCompatible` verifies the library's
csproj carries the flag.

## Trim warnings

`samples/Part11_2.TrimWarningLab` is an intentionally-broken sample, NOT in the
solution and NOT built by CI. `scripts/aot/publish-trim-warning-lab.sh` publishes
it and asserts IL2026/IL3050 appear, then shows the fix order:

1. **Eliminate reflection** (preferred). `samples/.../Fixed/Program.cs` calls
   `Guid.NewGuid()` directly instead of reflecting over `System.Guid`.
2. **`[DynamicallyAccessedMembers]`** on parameters/returns when the type is
   known at the call site, telling the trim analyzer what to keep.
3. **`[RequiresUnreferencedCode]`** to propagate the warning to callers when
   the method genuinely needs reflection and cannot be made trim-safe.
4. **`[UnconditionalSuppressMessage]`** ONLY when proven safe, in minimal
   scope. Never as a first resort.

The AOT app itself must publish with zero IL2026/IL3050. The Linux CI gate
treats those warnings as failures, not noise.

## What this lab does NOT do

- No EF Core AOT. EF Core AOT support is experimental; this lab uses in-memory
  read-only data to avoid that risk surface.
- No cross-OS AOT. Linux native binaries are produced and tested on Linux only.
  Windows `win-x64` AOT publish would require the Windows native toolchain and
  runs on Windows only.
- No claim that the AOT app replaces the full Capstone. It is a constrained
  query/validate API designed to demonstrate the compilation model.