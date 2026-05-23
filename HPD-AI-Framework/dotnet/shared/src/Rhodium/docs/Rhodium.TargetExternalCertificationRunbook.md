# Rhodium Target And External Certification Runbook

This runbook covers the evidence that the local Rhodium verifier intentionally
does not claim:

- the named target-hardware performance gate
- broker or venue parity against external reference data

The local verifier remains the first gate. Do not start target or external
certification from a tree that cannot pass `verify-rhodium.cs`.

## 1. Target-Hardware Performance Certification

Run this on the named certification machine, not on a developer laptop.

Required machine class:

- final target host or a documented equivalent
- 64 logical processors for the proposal gate
- fixed power/performance profile
- no unrelated high-load jobs during the run
- clean tracked git state

Command:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --keep-reports --require-clean-git --require-target-hardware --report-dir artifacts/rhodium-certification
```

For the final release evidence run, use the combined release gate after the
external parity manifest has been prepared:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --keep-reports --require-clean-git --require-target-hardware --external-parity-manifest artifacts/rhodium-certification/external-parity-manifest.json --require-external-parity --require-release-evidence --report-dir artifacts/rhodium-certification
```

Or use the release helper to build the manifest from the hash-free spec and run
the same strict gate in one command:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/certify-rhodium-release.cs -- --spec artifacts/rhodium-certification/external-parity-spec.json --report-dir artifacts/rhodium-certification
```

Required retained artifacts:

- `artifacts/rhodium-certification/vector-smoke-report.json`
- `artifacts/rhodium-certification/replay-certification-smoke.json`
- `artifacts/rhodium-certification/rhodium-certification-manifest.json`
- git commit hash for the run
- host identity and hardware notes

Pass criteria:

- verifier exits `0`
- manifest `ReportContractsValidated` is `true`
- manifest `RequireCleanGit` is `true`
- manifest `RequireTargetHardware` is `true`
- manifest `VerifierArguments` matches the command used
- vector report `VariantCount` is `10000`
- vector report `BarCount` is `100`
- vector report `Passed` is `true`
- vector report `LogicalProcessorCount` is at least `64`
- vector report elapsed time is under the five-minute ceiling
- replay report contains all eight certification smoke scenarios and all pass

If the processor count is below 64, `--require-target-hardware` fails the verifier.
The run may still be useful as local evidence without that flag, but it does not
certify the proposal's target-hardware performance claim.

## 2. External Broker And Venue Parity Certification

External parity requires named data sources. Synthetic or bundled fixtures are
not enough.

Required external fixture classes and manifest `FixtureKind` values:

- `TradingCalendar`: broker-maintained trading calendars and special closures
- `AccountStatement`: broker or clearing statements for cash, custody, settlement, and corporate actions
- `MarginLiquidationFinancing`: broker margin, liquidation, and financing examples
- `MarketReplayExecution`: exchange or broker replay datasets for order-book, quote, trade, and execution behavior
- `VenueOrderPolicy`: venue order policy feeds for order types, routing constraints, and rejection reasons
- `CrossVenueRouting`: cross-venue routing examples with expected route decisions

For each fixture, record:

- provider or venue name
- dataset id or export id
- covered date range
- instrument set
- account type
- expected result source
- Rhodium command or test that consumed it
- artifact paths for input, output, and comparison report

The verifier can enforce this evidence by validating an external parity manifest:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/verify-rhodium.cs --keep-reports --require-clean-git --external-parity-manifest artifacts/rhodium-certification/external-parity-manifest.json --require-external-parity --report-dir artifacts/rhodium-certification
```

To avoid hand-writing SHA-256 digests, prepare a manifest spec with the artifact
paths, then let the builder write the final manifest:

```bash
dotnet run HPD-AI-Framework/dotnet/shared/src/Rhodium/eng/ci/build-external-parity-manifest.cs -- --spec artifacts/rhodium-certification/external-parity-spec.json --out artifacts/rhodium-certification/external-parity-manifest.json
```

The builder resolves fixture artifact paths relative to the output manifest
directory, requires those artifacts to stay inside that directory, computes
SHA-256 digests for each input, output, and comparison artifact, fills
`GitCommit` from the current checkout when it is omitted or empty, and writes the
final verifier-ready manifest.

For the final release run, prefer `certify-rhodium-release.cs`; it invokes this
builder first and then invokes `verify-rhodium.cs` with the strict release
evidence flags. The lower-level builder and verifier commands remain documented
so CI and auditors can inspect each step independently.

The formal JSON Schema is available at
`docs/templates/Rhodium.ExternalParityManifest.schema.json`. The example manifest
is available at `docs/templates/Rhodium.ExternalParityManifest.example.json`.
A hash-free builder spec example is available at
`docs/templates/Rhodium.ExternalParityManifest.spec.example.json`, with schema at
`docs/templates/Rhodium.ExternalParityManifestSpec.schema.json`. The examples are
shape documentation, not passing release evidence; replace the placeholder paths
with real artifacts before verifier use.

Manifest shape:

```json
{
  "ReportVersion": 1,
  "Passed": true,
  "Provider": "Provider or venue name",
  "DatasetId": "Provider export or replay dataset id",
  "GitCommit": "Full Rhodium commit or 12+ character prefix used for the comparison",
  "AcceptedLimitations": [],
  "UnsupportedFeatures": [],
  "Mismatches": [
    {
      "Name": "Mismatch name",
      "Description": "Observed difference and expected behavior",
      "Classification": "ProviderDataAmbiguity"
    }
  ],
  "Fixtures": [
    {
      "Name": "Fixture name",
      "FixtureKind": "TradingCalendar",
      "Provider": "Provider or venue name",
      "DatasetId": "Provider export or replay dataset id",
      "ExpectedResultSource": "Statement, replay, or policy source",
      "CoveredDateRange": "YYYY-MM-DD/YYYY-MM-DD",
      "InstrumentSet": "Symbols or fixture instrument set id",
      "AccountType": "Cash, margin, futures, options, or provider account type",
      "Passed": true,
      "InputArtifactPath": "inputs/provider-export.json",
      "InputArtifactSha256": "64-character SHA-256 hex digest",
      "OutputArtifactPath": "outputs/rhodium-output.json",
      "OutputArtifactSha256": "64-character SHA-256 hex digest",
      "ComparisonReportPath": "reports/comparison-report.json",
      "ComparisonReportSha256": "64-character SHA-256 hex digest"
    }
  ]
}
```

`AcceptedLimitations`, `UnsupportedFeatures`, and `Mismatches` are required
arrays. Use empty arrays when there is nothing to report. Mismatch
`Classification` must be one of `ProviderDataAmbiguity`, `PolicyDifference`, or
`UnsupportedFeature`. A passed external parity manifest may not contain a
`RhodiumBug` mismatch; that is a failing certification result, not accepted
evidence.

Every manifest must include at least one passed fixture for each required
`FixtureKind`: `TradingCalendar`, `AccountStatement`,
`MarginLiquidationFinancing`, `MarketReplayExecution`, `VenueOrderPolicy`, and
`CrossVenueRouting`. This prevents a partial provider comparison from being
mistaken for full external parity evidence.

`GitCommit` must match the current checkout used by `verify-rhodium.cs`. A full
commit hash or a 12+ character prefix is accepted.

`InputArtifactPath`, `OutputArtifactPath`, and `ComparisonReportPath` are
required for every fixture. The verifier requires each path to be a non-empty
string and requires the referenced file to exist. Relative paths are resolved
from the external parity manifest directory so the evidence bundle can move as a
unit. Absolute paths are only accepted when they still point inside the manifest
directory; evidence files outside the bundle are rejected.

`InputArtifactSha256`, `OutputArtifactSha256`, and `ComparisonReportSha256` are
also required for every fixture. The verifier recomputes SHA-256 for each
artifact and rejects the manifest if any digest does not match.

The JSON Schema can catch shape errors before the verifier runs. The verifier is
still authoritative for checks that depend on the current checkout and filesystem,
including git commit matching, artifact path existence, artifact containment, and
artifact hash matching. The verifier also rejects unknown manifest, fixture, and
mismatch properties so typoed evidence fields do not silently pass.

Pass criteria:

- Rhodium output matches the provider reference for the documented tolerance
- every mismatch is classified as provider-data ambiguity, policy difference, or
  unsupported feature
- no known Rhodium bug mismatch remains in a passed manifest
- unsupported features become explicit backlog items, not silent pass cases
- parity reports are retained with the same git commit hash as the local verifier
  run

## 3. Release Evidence Bundle

A release evidence bundle should contain:

- local verifier manifest and reports
- target-hardware verifier manifest and reports
- external parity comparison reports
- input dataset manifest
- git commit hash
- clean tracked git-state proof
- list of accepted limitations
- list of unsupported provider features

The verifier enforces the bundle shape when `--require-release-evidence` is
enabled: reports must be retained, tracked git must be clean, target hardware
must be required, external parity must be required, `--report-dir` must be
supplied, the external parity manifest must live inside `--report-dir`, and all
local gates must run.

The release claim should say exactly which level passed:

- local Rhodium certification
- target-hardware Rhodium certification
- named broker or venue parity certification

Do not use a local verifier pass as shorthand for broker or venue parity.
