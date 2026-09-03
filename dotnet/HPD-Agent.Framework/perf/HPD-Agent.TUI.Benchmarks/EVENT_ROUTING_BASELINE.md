# Agent Event Routing Baseline

Captured 2026-09-03 with BenchmarkDotNet 0.15.6 on Apple M4, macOS 26.4.1,
.NET 10.0.9 Arm64 RyuJIT, concurrent workstation GC. Each case owns an independent
coordinator and inbox, and route construction is excluded consistently.

```text
| Method      | Mean     | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------ |---------:|------:|-------:|----------:|------------:|
| LocalOnly   | 58.26 ns |  1.00 | 0.0105 |      88 B |        1.00 |
| ExactThread | 80.46 ns |  1.38 | 0.0229 |     192 B |        2.18 |
| FullSubtree | 79.05 ns |  1.36 | 0.0229 |     192 B |        2.18 |
```

Reproduce the short baseline from `dotnet/HPD-Agent.Framework`:

```bash
dotnet run --project perf/HPD-Agent.TUI.Benchmarks/HPD-Agent.TUI.Benchmarks.csproj \
  -c Release -f net10.0 -- \
  --filter '*AgentEventRoutingBenchmark*' --job short --exporters markdown
```

These figures are a regression reference, not a cross-machine performance promise.
