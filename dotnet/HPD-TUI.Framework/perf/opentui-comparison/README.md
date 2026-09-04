# OpenTUI matched comparison

This executable harness invokes the checked-out OpenTUI core API directly and its React reconciler separately. Both
adapters execute the complete matched core matrix at 80x24, 120x40, and 240x80: no-op, one-cell, one-row,
two disjoint rows, full repaint, cursor-only, style-only, wide grapheme, hyperlink destination, and resize. Setup is
timed separately. Per-operation output bytes are the actual ANSI/control bytes emitted by OpenTUI's native stdout
feed, not the size of a captured character frame. JSON uses `hpd.tui.framework-comparison.v1` and is joinable with
HPD results on `adapter`, `scenario`, `width`, and `height`.

From this directory, install the React peer dependencies once and run:

```sh
bun install --frozen-lockfile
OPENTUI_REFERENCE=/absolute/path/to/opentui bun run benchmark
```

Set `BENCHMARK_WARMUP` and `BENCHMARK_ITERATIONS` identically for both frameworks. Generate HPD's side with:

```sh
dotnet run --project ../HPD-TUI.Benchmarks -c Release -- --evidence \
  --warmup 30 --iterations 200 --output ../results/hpd-after.json
```

The referenced OpenTUI checkout must have its own dependencies and native package prepared. The harness records its
exact Git commit and never substitutes a synthetic adapter when the reference is unavailable.

The HPD runner additionally executes component scaling, memory/delayed/backpressured/failure-recovery transport,
a real Unix PTY sink when `openpty(3)` is available, and the AoS-versus-SoA cell representation experiment. The
coding-agent benchmark project contains the nine proposal agent scenarios.
