# Markdown streaming baseline

Recorded 2026-09-03 on an Apple M4 running macOS 26.4.1 with .NET SDK
11.0.100-preview.7.26381.103. The command was:

```sh
dotnet run --project HPD-Agent.TUI.Benchmarks.csproj -c Release -f net10.0 --no-build -- \
  --filter '*MarkdownStreamingBenchmark*' --job Short
```

This is a sampled BenchmarkDotNet baseline with one launch, three warmups, and
three measured iterations. Release qualification should run the ordinary job and
retain its CSV/JSON output before tightening percentile or regression thresholds.

| Scenario | ShortRun mean | Allocated | Relative time |
|---|---:|---:|---:|
| Full message per delta | 40.587 ms | 93,183.02 KB | 1.000 |
| Coalesced full message | 0.135 ms | 244.81 KB | 0.003 |
| Newline gated | 6.619 ms | 15,233.46 KB | 0.163 |
| Stable prefix / mutable tail | 8.491 ms | 20,562.13 KB | 0.209 |
| Long code block | 2.265 ms | 6,813.11 KB | 0.056 |
| Growing tables | 128.784 ms | 254,199.57 KB | 3.174 |
| Long transcript with active message | 2.098 ms | 4,752.93 KB | 0.052 |
| Repeated resize publication source | 3.218 ms | 11,362.35 KB | 0.079 |
| Very large adversarial message | 152.631 ms | 9,632.36 KB | 3.762 |
| Over-budget event-loop workload | 1.370 ms | 4,306.86 KB | 0.034 |

The structured `MarkdownStreamDiagnosticsSnapshot` and
`MarkdownProjectionDiagnosticsSnapshot` counters provide parse/layout counts,
durations, invalidations, reuse, cache activity, and degradation counts without
including model source. Those values are the acceptance-harness inputs for the
80% parse-reduction, one-parse-per-publication, stable-layout reuse, and event-loop
stall guardrails.
