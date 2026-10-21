# HPD-Agent 0.9.1

## Fix

- Fixed `InMemoryAgentTuiRuntime` so the Agent allocates and journals each
  execution identity instead of the TUI adapter supplying one before admission.
- Removed duplicate TUI lifecycle events; Agent execution journaling is now the
  authoritative start/finish record.
- Failed or rejected submissions release the adapter admission slot without
  leaving a ghost execution or blocking the next input.

## Upgrade

Applications using the in-process TUI runtime should upgrade all HPD-Agent
package references from `0.9.0` to `0.9.1`.

## Validation

- Cross-target TUI runtime regression tests: passed.
- Single-file file reviewer restored, built, launched its TUI, and exited cleanly
  against the published `0.9.1` packages.

The `0.9.0` packages remain available but contain the execution-identity defect.
