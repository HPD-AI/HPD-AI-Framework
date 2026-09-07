# Compositor benchmark evidence

`results/hpd-after.json` is a frozen local result from the manifest runners. It contains raw per-scenario P95,
allocation, emitted-byte, cell/row-work, and retained-command metrics plus the CPU architecture, OS, runtime, GC,
tiered-PGO/ReadyToRun settings, corpus seed, warmups, iterations, and commit.

The frozen 120x40 compositor evidence records warm no-op at 1,000 ns P95, one-row mutation at 37,500 ns P95,
and full-screen repaint at 387,000 ns P95. The matched historical one-row baseline is 67,200 ns, so the retained
path is 44.2% faster and clears the required 30% relative improvement (47,040 ns maximum). The no-op and full
repaint cases remain inside their absolute/relative gates. Gen0 and Gen1 collection counts are recorded directly;
the one-row measurement reported neither collection. Allocation totals include each scenario's mutation workload
(the renderer's dedicated warmed allocation test remains zero-allocation).

The historical baseline is intentionally executable rather than reconstructed. Check out commit `1ab6a3517` into a
worktree and run `HPD-TUI.HistoricalBaseline` with `HpdTuiSourceRoot` pointing at that checkout's
`dotnet/HPD-TUI.Framework/src`; redirect stdout to `results/hpd-before.json`. Use 30 warmups and 200 measured
operations and set `BASELINE_COMMIT=1ab6a3517`. The checked-in artifacts use those 30/200 settings. Never compare results collected on different machines or runtime
settings.

Agent scenarios are produced by running `HPD-Agent.Harness.Coding.TUI.Benchmarks --evidence --warmup 30
--iterations 200`; its nine raw results are merged without transformation into the same `results` array and its
separate environment is retained as `agentEvidence`. Both runners record CPU, SDK, NativeAOT state, affinity and
power-control status. `cold-start` creates, first-renders, and disposes a fresh scene for every measured sample;
it does not reuse the warmed steady-state scene.

Absolute and relative P95 budgets are machine-readable in `benchmark-manifest.json`. OpenTUI results are joinable on
`scenario`, `width`, and `height`; an unavailable or unprepared checkout is reported as missing evidence and is never
replaced by estimated data.
