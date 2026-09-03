# Markdown streaming baseline

Recorded 2026-09-03 on an Apple M4 running macOS 26.4.1 with .NET SDK
11.0.100-preview.7.26381.103. The command was:

```sh
dotnet run --project HPD-Agent.TUI.Benchmarks.csproj -c Release -f net10.0 --no-build -- \
  --filter '*MarkdownStreamingBenchmark*' --job Dry
```

This is a cold-start smoke baseline (one measured iteration), not a statistically
stable performance gate. CI or release qualification must run the ordinary
BenchmarkDotNet job and retain its CSV/JSON output before enforcing percentile or
regression thresholds.

| Scenario | Dry mean | Allocated |
|---|---:|---:|
| Full message per delta | 238.064 ms | 63,874.64 KB |
| Coalesced full message | 25.362 ms | 193.21 KB |
| Newline gated | 54.542 ms | 10,472.94 KB |
| Stable prefix / mutable tail | 43.946 ms | 7,075.41 KB |
| Long code block | 89.537 ms | 540.83 KB |
| Growing tables | 681.040 ms | 278,090.01 KB |
| Long transcript with active message | 93.100 ms | 22,704.02 KB |
| Repeated resize publication source | 35.534 ms | 4,126.69 KB |
| Very large adversarial message | 239.219 ms | 2,988.48 KB |
| Over-budget event-loop workload | 81.126 ms | 18,689.00 KB |

The structured `MarkdownStreamDiagnosticsSnapshot` and
`MarkdownProjectionDiagnosticsSnapshot` counters provide parse/layout counts,
durations, invalidations, reuse, cache activity, and degradation counts without
including model source. Those values are the acceptance-harness inputs for the
80% parse-reduction, one-parse-per-publication, stable-layout reuse, and event-loop
stall guardrails.
