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
| Full message per delta | 43.698 ms | 93,183.02 KB | 1.000 |
| Coalesced full message | 0.139 ms | 244.82 KB | 0.003 |
| Newline gated | 7.093 ms | 15,233.48 KB | 0.162 |
| Stable prefix / mutable tail | 8.333 ms | 20,564.80 KB | 0.191 |
| Long code block | 2.412 ms | 6,813.18 KB | 0.055 |
| Growing tables | 125.556 ms | 254,186.87 KB | 2.873 |
| Finalized history, plain-text baseline | 0.044 ms | 85.80 KB | 0.001 |
| Finalized history, source-backed | 0.037 ms | 86.61 KB | 0.001 |
| Isolated active update over 1,000 finalized rows | 0.049 ms | 107.48 KB | 0.001 |
| Repeated resize publication source | 3.925 ms | 11,362.67 KB | 0.090 |
| Very large adversarial message | 153.391 ms | 9,632.52 KB | 3.510 |
| Serialized bounded event-loop overload | 3.433 ms | 7,956.48 KB | 0.079 |

The serialized overload run records callback queue latency independently of its
end-to-end workload time: p50 **9 μs**, p95 **132 μs**. Its dispatcher owns a
bounded 256-item queue, executes on a dedicated event-loop thread, periodically
drains nested refresh work, and measures enqueue-to-execution latency for input,
stream, repaint, and terminal work. The initial stall budget is p95 <= 1 ms on
this hardware.

The finalized source-backed path is 16.4% faster than the plain-text history
baseline (within the <=10% regression gate) with a 0.9% allocation increase.
The isolated active update allocates 107.48 KB while retaining 1,000 finalized
rows; the benchmark invocation creates only the new message session/document and
does not reconstruct the finalized transcript.

The structured `MarkdownStreamDiagnosticsSnapshot` and
`MarkdownProjectionDiagnosticsSnapshot` counters provide parse/layout counts,
durations, invalidations, reuse, cache activity, and degradation counts without
including model source. Those values are the acceptance-harness inputs for the
80% parse-reduction, one-parse-per-publication, stable-layout reuse, and event-loop
stall guardrails. The deterministic acceptance test requires at least 80% fewer
parses than deltas, parse count <= publication count, and nonzero stable-block
reuse; the same counters are published by production without model source.
