using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(BenchmarkEntrypoint).Assembly).Run(args);

internal static class BenchmarkEntrypoint;