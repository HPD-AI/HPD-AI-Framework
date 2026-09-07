# Production terminal smoke test

On Unix, run `python3 run.py` from this directory. It builds the small harness, opens a 24-column by 6-row pseudo-terminal, and executes the real `ProcessTerminal` and managed renderer with recognized terminal environment profiles. Captured output is checked by the same strict terminal oracle used in the conformance tests.

The checks cover capability detection, visible history above the live region, live edits, alternate-screen page return, and shutdown cursor/wrap restoration. Both synchronized and unsynchronized output paths run. Temporary captures are deleted after the run.

Profile names identify environment inputs, not launched terminal applications. This smoke test validates actual console/PTY I/O; manual testing in each supported terminal emulator remains separate.
