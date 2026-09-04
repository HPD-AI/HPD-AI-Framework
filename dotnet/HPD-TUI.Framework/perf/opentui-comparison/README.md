# OpenTUI matched comparison

This executable harness invokes the checked-out OpenTUI core API directly and its React reconciler separately. Both
adapters render the same 120x40 semantic scene and perform warm no-op, one-row mutation, and two-disjoint-row mutation
sequences. Setup is timed separately from steady-state samples; results contain per-operation mean, median, P95,
memory-rendered UTF-8 bytes, and post-GC heap delta.

From this directory, install the React peer dependencies once and run:

```sh
bun install --frozen-lockfile
OPENTUI_REFERENCE=/absolute/path/to/opentui bun run benchmark
```

The referenced OpenTUI checkout must have its own dependencies and native package prepared. The harness records its
exact Git commit and never substitutes a synthetic adapter when the reference is unavailable.
