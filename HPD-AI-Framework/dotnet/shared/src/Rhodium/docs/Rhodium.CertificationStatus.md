# Rhodium Certification Status

Rhodium has two different proof levels:

1. Local implementation gates that prove the in-tree runtime, generator, simulation, analytics, connectivity, and replay smoke scenarios work together.
2. External broker and venue parity certification, which requires named broker/exchange datasets and target hardware runs.

The first level is automated in this repository. The second level is intentionally not claimed by local smoke tests.

## Local Gate

Run the full local gate from the repository root:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs
```

Show verifier options:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs -- --help
```

List the verifier gates and replay certification scenarios:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --list-gates
```

The gate runs all Rhodium test projects, the vector smoke gate, and the replay certification smoke gate. It also removes Rhodium `bin` and `obj` directories before exiting successfully.

To keep the generated JSON reports for audit review:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --keep-reports
```

To rerun only the smoke gates and report-contract validation, skip the test matrix:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --skip-tests --keep-reports
```

To write reports to a dedicated artifact directory, use `--report-dir`:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --skip-tests --keep-reports --report-dir artifacts/rhodium-certification
```

For release certification runs, require a clean tracked git state:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --keep-reports --require-clean-git --report-dir artifacts/rhodium-certification
```

For target-hardware certification runs, also require the target hardware gate:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --keep-reports --require-clean-git --require-target-hardware --report-dir artifacts/rhodium-certification
```

For broker or venue parity certification runs, require an external parity manifest:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --keep-reports --require-clean-git --external-parity-manifest artifacts/rhodium-certification/external-parity-manifest.json --require-external-parity --report-dir artifacts/rhodium-certification
```

To build that manifest from a hash-free spec and compute artifact digests:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/build-external-parity-manifest.cs -- --spec artifacts/rhodium-certification/external-parity-spec.json --out artifacts/rhodium-certification/external-parity-manifest.json
```

For full release evidence runs, require retained reports, clean git, target hardware, external parity, an explicit artifact directory, and all local gates:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --keep-reports --require-clean-git --require-target-hardware --external-parity-manifest artifacts/rhodium-certification/external-parity-manifest.json --require-external-parity --require-release-evidence --report-dir artifacts/rhodium-certification
```

The release helper builds the external parity manifest from the hash-free spec and then runs that strict verifier command:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/certify-rhodium-release.cs -- --spec artifacts/rhodium-certification/external-parity-spec.json --report-dir artifacts/rhodium-certification
```

The verifier also accepts the CI-friendly equals form:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --skip-tests --keep-reports --report-dir=artifacts/rhodium-certification
```

The retained reports are written to:

```text
HPD-AI-Framework/dotnet/shared/src/Rhodium/benchmarks/vector-smoke-report.json
HPD-AI-Framework/dotnet/shared/src/Rhodium/benchmarks/replay-certification-smoke.json
HPD-AI-Framework/dotnet/shared/src/Rhodium/benchmarks/rhodium-certification-manifest.json
```

When `--report-dir` is supplied, the same filenames are written under that directory.

The manifest records a `CertificationRunId`, the exact verifier arguments, which gates ran, the Rhodium test project count, vector smoke dimensions, replay certification scenario count, whether report contracts were validated, whether `--require-clean-git` was enabled, whether `--require-target-hardware` was enabled, whether `--require-release-evidence` was enabled, whether `--require-external-parity` was enabled, the paths to generated smoke reports and supplied parity evidence, and SHA-256 digests for the vector smoke report, replay certification report, and external parity manifest when those artifacts are supplied. The verifier validates the manifest contract before cleanup, including that the recorded verifier arguments match the current invocation, report paths exist for gates that ran, report paths are null for gates that were skipped, and recorded artifact digests match the validated files.

Before cleanup, `verify-rhodium.cs` validates the report contract for all generated certification artifacts: report version, certification run id correlation, gate name, pass status, environment metadata, vector dimensions, vector five-minute elapsed ceiling, target-hardware processor count when requested, replay scenario count, replay scenario names, replay scenario evidence, external parity manifest evidence when supplied, required external fixture kinds, artifact containment, artifact SHA-256 digests, manifest verifier arguments, manifest gate flags, manifest counts, and manifest report paths.

Unknown verifier options fail the run instead of being ignored. This is intentional so CI typos do not accidentally skip certification work.

The verifier also rejects invocations that skip the test matrix, vector smoke, and replay certification smoke at the same time. At least one gate must run.

The verifier rejects `--require-target-hardware` combined with `--skip-vector-smoke`, because target-hardware certification is proven from the vector smoke report.

The verifier rejects `--require-external-parity` without `--external-parity-manifest`. Supplying `--external-parity-manifest` validates that manifest even when the external parity gate is not required. External parity manifests must include passed fixtures for all required `FixtureKind` values: `TradingCalendar`, `AccountStatement`, `MarginLiquidationFinancing`, `MarketReplayExecution`, `VenueOrderPolicy`, and `CrossVenueRouting`. Each fixture must also include existing input, output, and comparison report artifact paths inside the manifest directory, plus matching SHA-256 digests for those artifacts. A passed manifest may record provider-data ambiguities, policy differences, and unsupported features, but it cannot record a `RhodiumBug` mismatch.

The verifier rejects `--require-release-evidence` unless reports are retained, tracked git is clean, target hardware is required, external parity is required, `--report-dir` is supplied, the external parity manifest is inside `--report-dir`, and no local gate is skipped.

The vector smoke gate runs:

```text
10,000 variants x 100 bars
```

It records report version, gate name, machine/runtime metadata, git branch/commit metadata when available, logical processor count, configured parallelism, elapsed time, and pass/fail in an optional JSON report.

The replay certification smoke gate currently covers:

- bundled clearing-calendar special closures
- internal cash transfer accounting
- reduce-to-maintenance replay liquidation
- stock split and cash-dividend accounting
- bundled rate-derived financing charges
- cross-venue crossed-quote diagnostics
- cross-venue market sweep routing
- bundled provider-style routing and replay order-policy feeds

When invoked with `--replay-certification-report`, the report writes report version, gate name, machine/runtime metadata, git branch/commit metadata when available, and structured evidence fields for each scenario, such as balances, quantities, routing decisions, charge amounts, policy rejection reasons, and dataset ids.

## What The Local Gate Proves

A passing local gate proves that the current checked-out Rhodium tree can:

- compile and pass the in-tree test matrix
- run the generated `Strategy` hot path through Queue and Vector simulation coverage
- complete the local `10,000` variant vector smoke within the configured five-minute ceiling on the current machine
- run replay/accounting/multi-venue smoke scenarios end to end
- produce auditable replay certification evidence for the covered scenarios
- clean generated Rhodium build artifacts after verification

## What It Does Not Prove

A passing local gate does not prove:

- the named `10,000` variant benchmark on the final 64-core target hardware
- broker-maintained clearing-calendar completeness across all venues
- exact broker margin-call, liquidation, custody, and settlement workflows
- full venue-grade matching-engine sequencing and queue ownership
- parity against live broker or exchange replay datasets
- policy/feed completeness for every broker, venue, instrument class, or account type

Those require external fixtures, broker or venue reference data, and target-machine benchmark records.

Use `Rhodium.TargetExternalCertificationRunbook.md` for the required target-hardware and external parity evidence bundle.

## Current Boundary

The unified vector runtime proposal is implemented at the architecture level: generated `Strategy` authoring, parameter grids, event-major simulation, Queue and Vector execution models, rolling windows, analytics exports, deterministic live inbox processing, and shared event-boundary processing are present.

The remaining work is production certification and venue/broker parity. Treat it as a separate evidence program, not as more architecture churn.
