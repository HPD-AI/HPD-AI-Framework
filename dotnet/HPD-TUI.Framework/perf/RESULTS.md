# Compositor benchmark evidence

`results/hpd-after.json` is a frozen local result from the manifest runner. It contains raw per-scenario P95,
allocation, emitted-byte, cell/row-work, and retained-command metrics plus the CPU architecture, OS, runtime, GC,
tiered-PGO/ReadyToRun settings, corpus seed, warmups, iterations, and commit.

The historical baseline is intentionally executable rather than reconstructed. Check out commit `1ab6a3517` into a
worktree and run `HPD-TUI.HistoricalBaseline` with `HpdTuiSourceRoot` pointing at that checkout's
`dotnet/HPD-TUI.Framework/src`; redirect stdout to `results/hpd-before.json`. Use 30 warmups and 200 measured
operations and set `BASELINE_COMMIT=1ab6a3517`. The checked-in artifacts use those 30/200 settings. Never compare results collected on different machines or runtime
settings.

Absolute and relative P95 budgets are machine-readable in `benchmark-manifest.json`. OpenTUI results are joinable on
`scenario`, `width`, and `height`; an unavailable or unprepared checkout is reported as missing evidence and is never
replaced by estimated data.
