using System.Diagnostics;
using HPD.Payments.Tools.Conformance;

var canonicalBytes = await File.ReadAllBytesAsync("eng/registry/canonical-capabilities.json").ConfigureAwait(false);
var claimBytes = await File.ReadAllBytesAsync("eng/registry/claim-matrix.json").ConfigureAwait(false);
var snapshot = RegistrySnapshot.Load(canonicalBytes, claimBytes);
var dispositions = snapshot.Claims.Select(static claim => new RouteDisposition(claim.CanonicalId,
    claim.Applicability == "Blocked" ? RouteDispositionKind.Blocked : RouteDispositionKind.Untested,
    claim.Applicability == "Blocked" ? "RES-009 pending" : "not selected", Array.Empty<ProofCellKey>())).ToArray();
var manifest = new ReleaseManifest
{
    SchemaVersion = "hpd.payments.release-manifest.v1", CanonicalRegistryDigest = snapshot.CanonicalDigest,
    ClaimMatrixDigest = snapshot.ClaimMatrixDigest, SourceRevision = "performance-fixture",
    CreatedAtUtc = DateTimeOffset.UnixEpoch, PredecessorManifestDigest = "GENESIS",
    Lifecycle = ReleaseManifestLifecycle.Candidate, Dispositions = dispositions,
};
var manifestText = manifest.ToCanonicalText();
var cell = new ProofCellKey("TEST-001", "Scoped Identity", "identity-conformance", "OWN-01", "EXT-DET-01",
    "temp", "static", "inmemory-temp", "simulator", "local", "dev", "v1", "graph-1", "osx-arm64",
    "macos", "arm64", "10.0.301", "10.0", "csharp-14", "illink", "true", "happy", "perf");
var digest = "sha256:" + new string('a', 64);
var receipt = new ProofReceipt
{
    Cell = cell, SchemaVersion = "hpd.payments.proof.v1", ReceiptId = "perf-receipt", RunId = "perf-run",
    RouteId = cell.CanonicalId, SourceRevision = "performance-fixture", WholeTreeDigest = digest,
    DirtyState = "declared-performance-fixture", AdapterTreeDigest = digest,
    CanonicalRegistryDigest = snapshot.CanonicalDigest, ClaimMatrixDigest = snapshot.ClaimMatrixDigest,
    PredecessorDigest = "GENESIS", DependencyDigests = [], CommandBinding = digest, AssertionsDigest = digest,
    OracleBinding = "performance-fixture-v1", CodeRevision = "code", ConfigurationRevision = "config",
    CredentialRevision = "credential", ProtocolRevision = "protocol", PolicyRevision = "policy", CorpusDigest = digest,
    RootSeed = new string('b', 64), DerivedSeed = new string('c', 64), VirtualTimeTraceDigest = digest,
    FaultScheduleDigest = digest, StandardOutputDigest = digest, StandardErrorDigest = digest, ExitStatus = 0,
    StartedAtUtc = DateTimeOffset.UnixEpoch, EndedAtUtc = DateTimeOffset.UnixEpoch.AddTicks(1),
    ResourceObservations = "performance-fixture", Limitations = "non-gating", CleanupAttestation = "clean:" + digest,
    Provenance = "performance-fixture", State = ProofState.Executed, Lifecycle = ReceiptLifecycle.Active,
};

var observations = new[]
{
    Measure("registry-cold-load", 16, 200, () => RegistrySnapshot.Load(canonicalBytes, claimBytes).Routes.Count),
    Measure("release-selection-179", 128, 5_000, () =>
    {
        var result = ReleaseSelectionValidator.Validate(snapshot, dispositions);
        return result.InventoryValid ? result.Errors.Count + 1 : -1;
    }),
    Measure("release-manifest-roundtrip-179", 32, 1_000, () => ReleaseManifest.Parse(manifestText).Dispositions.Count),
    Measure("single-receipt-ledger-validation", 128, 5_000, () =>
        ProofLedgerValidator.Validate([receipt], [cell], snapshot.CanonicalDigest, snapshot.ClaimMatrixDigest).IsValid ? 1 : -1),
};

foreach (var observation in observations)
    await Console.Out.WriteLineAsync($"{observation.Path}|iterations={observation.Iterations}|allocatedBytes={observation.AllocatedBytes}|bytesPerOperation={observation.BytesPerOperation:F4}|elapsedTicks={observation.ElapsedTicks}|checksum={observation.Checksum}")
        .ConfigureAwait(false);
return observations.All(static x => x.AllocatedBytes >= 0 && x.Checksum != 0) ? 0 : 1;

static Measurement Measure(string path, int warmup, int iterations, Func<int> action)
{
    var checksum = 0;
    for (var i = 0; i < warmup; i++) checksum = unchecked(checksum * 31 + action());
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timestamp = Stopwatch.GetTimestamp();
    for (var i = 0; i < iterations; i++) checksum = unchecked(checksum * 31 + action());
    var elapsed = Stopwatch.GetTimestamp() - timestamp;
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    GC.KeepAlive(checksum);
    return new(path, iterations, allocated, (double)allocated / iterations, elapsed, checksum);
}

internal readonly record struct Measurement(string Path, int Iterations, long AllocatedBytes,
    double BytesPerOperation, long ElapsedTicks, int Checksum);
