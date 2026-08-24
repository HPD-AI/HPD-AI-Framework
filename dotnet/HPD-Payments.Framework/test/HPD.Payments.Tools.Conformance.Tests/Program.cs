using HPD.Payments.Tools.Conformance;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

if (!ReleaseCellBinding.ValidateAndExecute("validate-proof")) return 1;
var failures = new List<string>();
void Check(bool value, string message) { if (!value) failures.Add(message); }
const string CanonicalRegistryDigest = "sha256:3c623b0dfbf040f34e30dfdc15a20629ada211e5b9493495fbde5300b2326ca9";
const string ClaimMatrixDigest = "sha256:e2ba5f55f9fe10b0ef13f422d15640057d1c4eda0610d1a7ef67a212fe1b1a05";
var cell = new ProofCellKey("TEST-001", "scoped-identity", "identity-conformance", "OWN-01", "EXT-DET-01",
    "temp", "static", "inmemory-temp", "simulator", "local", "dev", "v1", "graph-1", "osx-arm64",
    "macos", "arm64", "10.0.301", "10.0", "csharp-14", "illink", "true", "happy", "seed-1");
var start = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
ProofReceipt Make(string predecessor = "GENESIS", ProofState state = ProofState.Executed, int exit = 0,
    string cleanup = "clean:sha256:" + "0" + "000000000000000000000000000000000000000000000000000000000000000",
    string registry = CanonicalRegistryDigest, string claims = ClaimMatrixDigest,
    string? supersedes = null, ProofCellKey? selectedCell = null) => new()
{
    Cell = selectedCell ?? cell, SchemaVersion = "hpd.payments.proof.v1", ReceiptId = "receipt-1", RunId = "run-1",
    RouteId = (selectedCell ?? cell).CanonicalId,
    SourceRevision = "rev", WholeTreeDigest = "sha256:" + new string('a', 64), DirtyState = "clean",
    AdapterTreeDigest = "sha256:" + new string('b', 64), CanonicalRegistryDigest = registry,
    ClaimMatrixDigest = claims, PredecessorDigest = predecessor, SupersedesDigest = supersedes, DependencyDigests = [],
    CommandBinding = "test-conformance-proof/argv", AssertionsDigest = "sha256:" + new string('c', 64), OracleBinding = "oracle-v1",
    CodeRevision = "code", ConfigurationRevision = "config", CredentialRevision = "credential",
    ProtocolRevision = "protocol", PolicyRevision = "policy", CorpusDigest = "sha256:" + new string('d', 64),
    RootSeed = new string('3', 64), DerivedSeed = new string('4', 64),
    VirtualTimeTraceDigest = "sha256:" + new string('e', 64), FaultScheduleDigest = "sha256:" + new string('f', 64),
    StandardOutputDigest = "sha256:" + new string('1', 64), StandardErrorDigest = "sha256:" + new string('2', 64), ExitStatus = exit,
    StartedAtUtc = start, EndedAtUtc = start.AddSeconds(1), ResourceObservations = "unmeasured-preparatory",
    Limitations = "temporary profile; no release claim", CleanupAttestation = cleanup,
    Provenance = "fixture/source/tree/command", State = state, Lifecycle = ReceiptLifecycle.Active
};

var valid = ProofLedgerValidator.Validate([Make()], [cell], CanonicalRegistryDigest, ClaimMatrixDigest);
Check(valid.IsValid, "valid exact-cell receipt was rejected");
Check(Enum.GetNames<ProofState>().SequenceEqual(new[] { "Inspected", "Compiled", "Generated", "Linked", "Executed", "Failed", "Untested" }),
    "executable proof-state vocabulary diverged from the frozen seven states");
Check(Make().ContentAddress() == Make().ContentAddress(), "content address is nondeterministic");
var parsedReceipt = ProofReceiptCodec.Parse(Make().ToCanonicalText());
Check(parsedReceipt.ToCanonicalText() == Make().ToCanonicalText() && parsedReceipt.ContentAddress() == Make().ContentAddress(),
    "canonical receipt did not round-trip through the strict decoder");
var malformedReceiptRejected = false;
try { _ = ProofReceiptCodec.Parse(Make().ToCanonicalText() + "1:x"); }
catch (InvalidDataException) { malformedReceiptRejected = true; }
Check(malformedReceiptRejected, "trailing canonical receipt field was admitted");
Check(ProofLedgerValidator.Validate([Make(registry: "stale")], [cell], CanonicalRegistryDigest, ClaimMatrixDigest).Errors.Contains("stale-registry-binding"), "stale registry passed");
Check(ProofLedgerValidator.Validate([Make(), Make()], [cell], CanonicalRegistryDigest, ClaimMatrixDigest).Errors.Contains("broken-predecessor-chain"), "broken chain passed");
Check(ProofLedgerValidator.Validate([Make(), Make(Make().ContentAddress())], [cell], CanonicalRegistryDigest, ClaimMatrixDigest).Errors.Contains("duplicate-current-cell"), "duplicate exact cell passed");
var badReplacement = Make(Make().ContentAddress(), supersedes: "missing-address");
Check(ProofLedgerValidator.Validate([Make(), badReplacement], [cell], CanonicalRegistryDigest, ClaimMatrixDigest).Errors.Contains("missing-replacement-target"), "missing supersession target passed");
Check(ProofLedgerValidator.Validate([Make(exit: 1)], [cell], CanonicalRegistryDigest, ClaimMatrixDigest).Errors.Contains("false-pass"), "nonzero false pass was accepted");
Check(ProofLedgerValidator.Validate([Make(cleanup: "failed")], [cell], CanonicalRegistryDigest, ClaimMatrixDigest).Errors.Contains("false-pass"), "failed cleanup passed");
Check(ProofLedgerValidator.Validate([], [cell], CanonicalRegistryDigest, ClaimMatrixDigest).Errors.Contains("missing-cell"), "missing cell passed");
Check(ProofLedgerValidator.Validate([Make() with { CommandBinding = "" }], [cell], CanonicalRegistryDigest, ClaimMatrixDigest)
    .Errors.Contains("missing-or-over-bound-receipt-field"), "empty command binding passed");
Check(ProofLedgerValidator.Validate([Make() with { AssertionsDigest = "not-a-digest" }], [cell], CanonicalRegistryDigest, ClaimMatrixDigest)
    .Errors.Contains("malformed-receipt-digest"), "malformed assertion digest passed");
Check(ProofLedgerValidator.Validate([Make()], [cell, cell], CanonicalRegistryDigest, ClaimMatrixDigest)
    .Errors.Contains("duplicate-expected-cell"), "duplicate expected proof cell passed");
var grouped = cell with { CanonicalId = "TEST-*" };
Check(ProofLedgerValidator.Validate([Make() with { Cell = grouped }], [cell], CanonicalRegistryDigest, ClaimMatrixDigest).Errors.Contains("orphan-cell"), "grouped proof substituted for route");

var schedule1 = DeterministicSchedule.Permute(32, 0x1234UL);
var schedule2 = DeterministicSchedule.Permute(32, 0x1234UL);
Check(schedule1.SequenceEqual(schedule2) && schedule1.Order().SequenceEqual(Enumerable.Range(0, 32)),
    "deterministic schedule changed or lost actions");
var shrinks = DeterministicSchedule.Shrink(schedule1);
Check(shrinks.Count > 0 && shrinks.All(x => x.Length < schedule1.Length) &&
    shrinks.Select(x => string.Join(',', x)).Distinct(StringComparer.Ordinal).Count() == shrinks.Count,
    "schedule shrinking was not bounded and deterministic");
var time = new ConformanceTimeProvider(start);
time.Advance(TimeSpan.FromSeconds(5));
Check(time.GetUtcNow() == start.AddSeconds(5), "virtual time did not advance exactly");
var input = "authenticated-payload"u8.ToArray();
var corpus1 = BoundedCorpus.Generate(input, 64, 0x5678UL);
var corpus2 = BoundedCorpus.Generate(input, 64, 0x5678UL);
Check(corpus1.Select(Convert.ToHexString).SequenceEqual(corpus2.Select(Convert.ToHexString)),
    "derived corpus is not reproducible");
var firstBefore = corpus1[0][0]; input[0] ^= 0xFF;
Check(corpus1[0][0] == firstBefore && corpus1.All(x => x.Length == "authenticated-payload"u8.Length),
    "corpus did not own bounded generated cases");
var storeRoot = Path.Combine(Path.GetTempPath(), $"hpd-payments-proof-store-{Environment.ProcessId}");
var concurrentStoreRoot = storeRoot + "-concurrent";
if (Directory.Exists(storeRoot)) Directory.Delete(storeRoot, recursive: true);
if (Directory.Exists(concurrentStoreRoot)) Directory.Delete(concurrentStoreRoot, recursive: true);
try
{
    var stored = ProofReceiptStore.Write(storeRoot, Make());
    var replay = ProofReceiptStore.Write(storeRoot, Make());
    var persistedCanonical = await File.ReadAllTextAsync(stored.Path).ConfigureAwait(false);
    var persistedReceipt = ProofReceiptCodec.Parse(persistedCanonical);
    Check(stored.Created && !replay.Created && stored.Path == replay.Path &&
        persistedCanonical == Make().ToCanonicalText() && persistedReceipt.ContentAddress() == stored.ContentAddress,
        "append-only receipt store did not preserve an exact replay");
    Check(!Directory.EnumerateFiles(storeRoot, "*.tmp", SearchOption.AllDirectories).Any(),
        "proof store retained a temporary file after success");
    var storedSecond = Make(stored.ContentAddress, selectedCell: cell with { Workload = "stored-second" }) with
        { ReceiptId = "receipt-stored-second", RunId = "run-stored-second" };
    _ = ProofReceiptStore.Write(storeRoot, storedSecond);
    var reloadedChain = ProofReceiptRepository.LoadChain(storeRoot);
    Check(reloadedChain.Count == 2 && reloadedChain[0].ContentAddress() == stored.ContentAddress &&
        reloadedChain[1].ContentAddress() == storedSecond.ContentAddress(),
        "unordered proof repository did not reconstruct the exact append-only chain");
    var forked = Make(stored.ContentAddress, selectedCell: cell with { Workload = "stored-fork" }) with
        { ReceiptId = "receipt-stored-fork", RunId = "run-stored-fork" };
    _ = ProofReceiptStore.Write(storeRoot, forked);
    var forkRejected = false;
    try { _ = ProofReceiptRepository.LoadChain(storeRoot); }
    catch (InvalidDataException) { forkRejected = true; }
    Check(forkRejected, "forked physical receipt chain was admitted");
    var concurrentWrites = await Task.WhenAll(Enumerable.Range(0, 32)
        .Select(_ => Task.Run(() => ProofReceiptStore.Write(concurrentStoreRoot, Make())))).ConfigureAwait(false);
    var concurrentCreated = concurrentWrites.Count(static x => x.Created);
    var concurrentPaths = concurrentWrites.Select(static x => x.Path).Distinct(StringComparer.Ordinal).Count();
    var concurrentTemps = Directory.EnumerateFiles(concurrentStoreRoot, "*.tmp", SearchOption.AllDirectories).Count();
    var concurrentLocks = Directory.EnumerateFiles(concurrentStoreRoot, "*.lock", SearchOption.AllDirectories).Count();
    Check(concurrentCreated == 1 && concurrentPaths == 1 && concurrentTemps == 0 && concurrentLocks == 0,
        $"concurrent exact receipt writers did not converge: created={concurrentCreated} paths={concurrentPaths} temps={concurrentTemps}");
    var cleanInventory = ProofArtifactInventory.Capture(concurrentStoreRoot);
    Check(cleanInventory.IsClean && cleanInventory.FileCount == 1 && cleanInventory.Entries.Count == 1,
        "clean proof-store receipt inventory was not admitted");
    var leakedBinary = Path.Combine(concurrentStoreRoot, "leak.bin");
    await File.WriteAllBytesAsync(leakedBinary, [0x7F, 0x45, 0x4C, 0x46]).ConfigureAwait(false);
    var dirtyInventory = ProofArtifactInventory.Capture(concurrentStoreRoot);
    Check(!dirtyInventory.IsClean && dirtyInventory.Errors.Any(static x => x.StartsWith("retained-non-receipt:", StringComparison.Ordinal)),
        "retained binary did not invalidate cleanup inventory");
    File.Delete(leakedBinary);
}
finally
{
    if (Directory.Exists(storeRoot)) Directory.Delete(storeRoot, recursive: true);
    if (Directory.Exists(concurrentStoreRoot)) Directory.Delete(concurrentStoreRoot, recursive: true);
}
Check(!Directory.Exists(storeRoot) && !Directory.Exists(concurrentStoreRoot), "proof-store fixture cleanup failed");
var sourceSnapshotRoot = Path.Combine(Path.GetTempPath(), $"hpd-payments-source-snapshot-{Environment.ProcessId}");
if (Directory.Exists(sourceSnapshotRoot)) Directory.Delete(sourceSnapshotRoot, recursive: true);
Directory.CreateDirectory(Path.Combine(sourceSnapshotRoot, "src"));
await File.WriteAllTextAsync(Path.Combine(sourceSnapshotRoot, "src", "a.cs"), "sealed class A {}\n").ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(sourceSnapshotRoot, "src", "b.cs"), "sealed class B {}\n").ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(sourceSnapshotRoot, "src", "packages.lock.json"), "{}\n").ConfigureAwait(false);
Directory.CreateDirectory(Path.Combine(sourceSnapshotRoot, "src", "bin", "Debug"));
Directory.CreateDirectory(Path.Combine(sourceSnapshotRoot, "src", "obj"));
await File.WriteAllTextAsync(Path.Combine(sourceSnapshotRoot, "src", "bin", "Debug", "generated.dll"), "one").ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(sourceSnapshotRoot, "src", "obj", "generated.cache"), "one").ConfigureAwait(false);
var sourceBefore = SourceTreeSnapshotter.Capture(sourceSnapshotRoot, ["src"]);
var sourceReplay = SourceTreeSnapshotter.Capture(sourceSnapshotRoot, ["src"]);
SourceTreeSnapshotter.RequireStable(sourceBefore, sourceReplay);
await File.WriteAllTextAsync(Path.Combine(sourceSnapshotRoot, "src", "bin", "Debug", "generated.dll"), "two").ConfigureAwait(false);
await File.WriteAllTextAsync(Path.Combine(sourceSnapshotRoot, "src", "obj", "generated.cache"), "two").ConfigureAwait(false);
SourceTreeSnapshotter.RequireStable(sourceBefore, SourceTreeSnapshotter.Capture(sourceSnapshotRoot, ["src"]));
var commandManifestBytes = await File.ReadAllBytesAsync("eng/commands/commands.json").ConfigureAwait(false);
var commandManifest = CommandManifestSnapshot.Load(commandManifestBytes);
commandManifest.RequireProductRoot(Directory.GetCurrentDirectory());
bool RejectManifest(Action<JsonObject> mutate)
{
    var document = JsonNode.Parse(commandManifestBytes)!.AsObject();
    mutate(document);
    try { _ = CommandManifestSnapshot.Load(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document)); return false; }
    catch (InvalidDataException) { return true; }
}
var escapedCwdRejected = RejectManifest(static document =>
    document["commands"]!.AsArray()[0]!["cwd"] = "../escape");
var duplicatePrerequisiteRejected = RejectManifest(static document =>
    document["commands"]!.AsArray()[0]!["prerequisites"] = new JsonArray("same-gate", "same-gate"));
var cyclicPrerequisiteRejected = RejectManifest(static document =>
{
    var commands = document["commands"]!.AsArray();
    commands[0]!["prerequisites"] = new JsonArray(commands[1]!["id"]!.GetValue<string>());
    commands[1]!["prerequisites"] = new JsonArray(commands[0]!["id"]!.GetValue<string>());
});
var escapedCleanupRejected = RejectManifest(static document =>
    document["commands"]!.AsArray()[0]!["cleanup"] = new JsonArray("../outside/**"));
var admittedLocalCommand = commandManifest.RequireEnabled("test-conformance-bootstrap-inmemory");
var cleanExecution = ExecutionCleanupSnapshotter.Capture(sourceSnapshotRoot, admittedLocalCommand.Cleanup);
var passingAssertions = new ProofAssertionOutcome("sha256:" + new string('5', 64), 6, 6, 0, 0, 0, 0, 0);
var admittedCandidate = Make() with { WholeTreeDigest = sourceBefore.InventoryDigest,
    CommandBinding = admittedLocalCommand.Binding, CleanupAttestation = cleanExecution.Attestation,
    AssertionsDigest = passingAssertions.EvidenceDigest };
_ = ProofRunAdmission.Admit(admittedCandidate, sourceBefore, sourceReplay, admittedLocalCommand, cleanExecution,
    passingAssertions);
var admittedProof = commandManifest.RequireEnabled("test-conformance-proof");
var mismatchedProductRootRejected = false;
try { commandManifest.RequireProductRoot(sourceSnapshotRoot); }
catch (InvalidDataException) { mismatchedProductRootRejected = true; }
Check(commandManifest.Revision == 37 && commandManifest.Commands.Count == 73 && admittedProof.Id == "test-conformance-proof" &&
    mismatchedProductRootRejected && escapedCwdRejected && duplicatePrerequisiteRejected &&
    cyclicPrerequisiteRejected && escapedCleanupRejected,
    "command manifest inventory changed or disabled proof command was admitted");
await File.AppendAllTextAsync(Path.Combine(sourceSnapshotRoot, "src", "a.cs"), "// changed\n").ConfigureAwait(false);
var sourceAfter = SourceTreeSnapshotter.Capture(sourceSnapshotRoot, ["src"]);
var sourceDriftRejected = false;
try { SourceTreeSnapshotter.RequireStable(sourceBefore, sourceAfter); }
catch (InvalidDataException) { sourceDriftRejected = true; }
Check(sourceBefore.FileCount == 3 && sourceBefore.InventoryDigest.StartsWith("sha256:", StringComparison.Ordinal) &&
    sourceDriftRejected, "source tree snapshot did not reproduce or reject byte drift");
var driftedRunRejected = false;
try { _ = ProofRunAdmission.Admit(admittedCandidate, sourceBefore, sourceAfter, admittedLocalCommand, cleanExecution,
    passingAssertions); }
catch (InvalidDataException) { driftedRunRejected = true; }
Check(driftedRunRejected, "source drift during an otherwise successful command produced an admitted receipt");
var skippedAssertions = new ProofAssertionOutcome("sha256:" + new string('5', 64), 6, 5, 0, 1, 0, 0, 0);
var malformedAssertionInventoryRejected = false;
try { new ProofAssertionOutcome("sha256:" + new string('z', 64), 1, 1, 0, 0, 0, 0, 0).Validate(); }
catch (InvalidDataException) { malformedAssertionInventoryRejected = true; }
var skippedRunRejected = false;
try { _ = ProofRunAdmission.Admit(admittedCandidate with { AssertionsDigest = skippedAssertions.EvidenceDigest },
    sourceBefore, sourceReplay, admittedLocalCommand, cleanExecution, skippedAssertions); }
catch (InvalidDataException) { skippedRunRejected = true; }
Check(skippedRunRejected && malformedAssertionInventoryRejected,
    "skipped or malformed assertion evidence produced an admitted Executed receipt");
Directory.Delete(sourceSnapshotRoot, recursive: true);
var cleanupRoot = Path.Combine(Path.GetTempPath(), $"hpd-payments-cleanup-{Environment.ProcessId}");
Directory.CreateDirectory(Path.Combine(cleanupRoot, "src", "sample", "bin", "Release"));
await File.WriteAllBytesAsync(Path.Combine(cleanupRoot, "src", "sample", "bin", "Release", "retained.dll"), [1, 2, 3])
    .ConfigureAwait(false);
var dirtyCleanup = ExecutionCleanupSnapshotter.Capture(cleanupRoot, ["**/bin/**"]);
Directory.Delete(Path.Combine(cleanupRoot, "src", "sample", "bin"), recursive: true);
var cleanCleanup = ExecutionCleanupSnapshotter.Capture(cleanupRoot, ["**/bin/**"]);
Check(!dirtyCleanup.IsClean && dirtyCleanup.Residue.Any(static x => x.EndsWith("retained.dll", StringComparison.Ordinal)) &&
    cleanCleanup.IsClean && cleanCleanup.Attestation.StartsWith("clean:sha256:", StringComparison.Ordinal),
    "declared cleanup residue was not rejected or clean attestation was not reproducible");
Directory.Delete(cleanupRoot, recursive: true);
var resourceBudget = new ResourceClaimBudget(cell.Graph, cell.Rid, cell.Path, cell.Workload, 1_000,
    64, 1, 10_000, 4_096, 32, 64);
var completeResource = new ResourceClaimObservation(1_000, 32_000, 500, 5_000, 1_000_000_000,
    2_048, 16, 32, 100, 100, 100);
Check(ResourceClaimValidator.Validate(resourceBudget, completeResource).IsWithinBudget,
    "complete scoped resource observation was rejected");
var unmeasuredResource = completeResource with { AllocationCount = null };
Check(ResourceClaimValidator.Validate(resourceBudget, unmeasuredResource).Errors.Contains("incomplete-resource-observation"),
    "unmeasured allocation count passed a resource budget");
var overBudgetResource = completeResource with { AllocatedBytes = 128_000, PoolClears = 99 };
var overBudgetResult = ResourceClaimValidator.Validate(resourceBudget, overBudgetResource);
Check(overBudgetResult.Errors.Contains("allocated-byte-budget-missed") &&
    overBudgetResult.Errors.Contains("pool-rent-return-clear-imbalance"),
    "missed byte budget or uncleared pool passed a resource claim");
var aotDigest = "sha256:" + new string('6', 64);
var aotEvidence = new AotClaimEvidence(cell.Graph, cell.Rid, cell.OperatingSystem, cell.Architecture, cell.Sdk,
    cell.Runtime, cell.Compiler, cell.Linker, 0, 0, 0, 0, 0, aotDigest, aotDigest, aotDigest, aotDigest);
Check(AotClaimValidator.Validate(cell, aotEvidence).Count == 0, "exact synthetic AOT evidence was rejected");
Check(AotClaimValidator.Validate(cell with { Rid = "linux-x64" }, aotEvidence)
    .Contains("aot-cell-toolchain-mismatch"), "one RID's AOT evidence certified another RID");
Check(AotClaimValidator.Validate(cell, aotEvidence with { ReflectionFallbackCount = 1 })
    .Contains("static-graph-reflection-fallback"), "reflection fallback passed a static AOT claim");
var maliciousRoot = storeRoot + "-malicious";
var escapeTarget = storeRoot + "-escape-target";
Directory.CreateDirectory(maliciousRoot); Directory.CreateDirectory(escapeTarget);
var receiptPrefix = Make().ContentAddress()[..2];
Directory.CreateSymbolicLink(Path.Combine(maliciousRoot, receiptPrefix), escapeTarget);
var symlinkEscapeRejected = false;
try { _ = ProofReceiptStore.Write(maliciousRoot, Make()); }
catch (IOException) { symlinkEscapeRejected = true; }
Check(symlinkEscapeRejected && !Directory.EnumerateFiles(escapeTarget).Any(),
    "symlinked digest-prefix directory escaped the proof root");
Directory.Delete(maliciousRoot, recursive: true); Directory.Delete(escapeTarget, recursive: true);
Check(Enum.GetValues<HistoryStage>().Length == 14 &&
    Enum.GetValues<HistoryStage>().Select(static x => (int)x).SequenceEqual(Enumerable.Range(0, 14)),
    "H0-H13 vocabulary is incomplete or unordered");
var completeFaults = FaultSchedule.Complete();
Check(Enum.GetValues<FaultBoundary>().Length == 24 && completeFaults.Coordinates.Count == 48 &&
    completeFaults.Coordinates.Distinct().Count() == 48 &&
    completeFaults.ToCanonicalText().StartsWith("H7:CompareBind:Before|H7:CompareBind:After", StringComparison.Ordinal) &&
    completeFaults.ToCanonicalText().EndsWith("H7:CustodyUpdate:After", StringComparison.Ordinal),
    "frozen H7 before/after matrix is incomplete or unstable");
var duplicateFaultRejected = false;
try { _ = new FaultSchedule([new(FaultBoundary.Claim, FaultSide.Before), new(FaultBoundary.Claim, FaultSide.Before)]); }
catch (ArgumentException) { duplicateFaultRejected = true; }
Check(duplicateFaultRejected, "duplicate H7 coordinate was admitted");
var rootSeed = Enumerable.Range(0, 32).Select(static x => (byte)x).ToArray();
var derived1 = ProofSeed.Derive(rootSeed, cell);
var derived2 = ProofSeed.Derive(rootSeed, cell);
var otherDerived = ProofSeed.Derive(rootSeed, cell with { Workload = "seed-2" });
rootSeed[0] ^= 0xFF;
Check(derived1.Length == 32 && derived1.SequenceEqual(derived2) && !derived1.SequenceEqual(otherDerived),
    "256-bit per-cell seed derivation is unstable or fails domain separation");
var sink = 0;
var observation = ResourceProbe.Measure(() => sink++, 16, 1_000);
Check(sink == 1_016 && observation.Iterations == 1_000 && observation.SameThreadAllocatedBytes >= 0 &&
    observation.Elapsed >= TimeSpan.Zero && observation.ManagedThreadId == Environment.CurrentManagedThreadId &&
    observation.GCSettings.Contains("latency=", StringComparison.Ordinal),
    "resource observation lost its exact measurement scope");
var canaryBytes = "synthetic-hpd-secret-canary-2030"u8.ToArray();
var canary = new SecretCanary(canaryBytes);
Check(canary.IsExposed(canaryBytes) &&
    canary.IsExposed(System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(canaryBytes))) &&
    canary.IsExposed(System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(canaryBytes))) &&
    !canary.IsExposed("redacted-output"u8), "secret canary representations were not detected exactly");
canary.Clear();
Check(!canary.IsExposed(canaryBytes), "cleared canary retained a detectable representation");
var canonicalRegistryBytes = await File.ReadAllBytesAsync("eng/registry/canonical-capabilities.json").ConfigureAwait(false);
var claimMatrixBytes = await File.ReadAllBytesAsync("eng/registry/claim-matrix.json").ConfigureAwait(false);
var snapshot = RegistrySnapshot.Load(canonicalRegistryBytes, claimMatrixBytes);
Check(snapshot.Routes.Count == 179 && snapshot.Claims.Count == 179 &&
    snapshot.Routes.Select(static x => x.Prefix).Distinct(StringComparer.Ordinal).Count() == 33 &&
    snapshot.Routes.SelectMany(static x => x.AuthorityOwners).Distinct(StringComparer.Ordinal).Count() == 17 &&
    snapshot.Routes.SelectMany(static x => x.Workflows).Distinct().Count() == 20 &&
    snapshot.Claims.Count(static x => x.Res009Status == "AcceptedPendingImplementation") == 28 &&
    snapshot.CanonicalDigest == "sha256:3c623b0dfbf040f34e30dfdc15a20629ada211e5b9493495fbde5300b2326ca9" &&
    snapshot.ClaimMatrixDigest == "sha256:e2ba5f55f9fe10b0ef13f422d15640057d1c4eda0610d1a7ef67a212fe1b1a05",
    "cold-path C# registry snapshot did not reproduce the frozen 179-row baseline");
var tamperedClaims = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(claimMatrixBytes)
    .Replace("ApplicablePendingSelection", "ApplicableTamperedSelection", StringComparison.Ordinal));
var tamperRejected = false;
try { _ = RegistrySnapshot.Load(canonicalRegistryBytes, tamperedClaims); }
catch (InvalidDataException) { tamperRejected = true; }
Check(tamperRejected, "claim content changed without digest update and was admitted");
var currentDispositions = snapshot.Claims.Select(static claim => new RouteDisposition(claim.CanonicalId,
    claim.Applicability == "Blocked" ? RouteDispositionKind.Blocked : RouteDispositionKind.Untested,
    claim.Applicability == "Blocked" ? "RES-009 explicit acceptance pending" : "exact release tuple and receipt not selected",
    Array.Empty<ProofCellKey>())).ToArray();
var currentSelection = ReleaseSelectionValidator.Validate(snapshot, currentDispositions);
Check(currentSelection.InventoryValid && !currentSelection.ReleaseComplete,
    "honest current 179-route inventory was rejected or falsely declared releasable");
var releaseManifest = new ReleaseManifest { SchemaVersion = "hpd.payments.release-manifest.v1",
    CanonicalRegistryDigest = snapshot.CanonicalDigest, ClaimMatrixDigest = snapshot.ClaimMatrixDigest,
    SourceRevision = "synthetic-fixture", CreatedAtUtc = start, PredecessorManifestDigest = "GENESIS",
    SupersedesManifestDigest = null, Lifecycle = ReleaseManifestLifecycle.Candidate, Dispositions = currentDispositions };
var reloadedManifest = ReleaseManifest.Parse(releaseManifest.ToCanonicalText());
var reloadedSelection = reloadedManifest.ValidateAgainst(snapshot);
Check(reloadedManifest.ToCanonicalText() == releaseManifest.ToCanonicalText() &&
    reloadedManifest.ContentAddress() == releaseManifest.ContentAddress() && reloadedSelection.InventoryValid &&
    !reloadedSelection.ReleaseComplete, "release manifest did not round-trip to the same honest inventory");
var trailingManifestRejected = false;
try { _ = ReleaseManifest.Parse(releaseManifest.ToCanonicalText() + "1:x"); }
catch (InvalidDataException) { trailingManifestRejected = true; }
Check(trailingManifestRejected, "release manifest trailing field was admitted");
var mismatchedManifestRejected = false;
try { _ = (releaseManifest with { CanonicalRegistryDigest = "sha256:stale" }).ValidateAgainst(snapshot); }
catch (InvalidDataException) { mismatchedManifestRejected = true; }
Check(mismatchedManifestRejected, "release manifest with a stale registry binding was admitted");
var incompletePublishedRejected = false;
try { _ = (releaseManifest with { Lifecycle = ReleaseManifestLifecycle.Published }).ValidateAgainst(snapshot); }
catch (InvalidDataException) { incompletePublishedRejected = true; }
Check(incompletePublishedRejected, "incomplete candidate manifest was published");
var targetlessWithdrawalRejected = false;
try { _ = (releaseManifest with { Lifecycle = ReleaseManifestLifecycle.Withdrawal }).ValidateAgainst(snapshot); }
catch (InvalidDataException) { targetlessWithdrawalRejected = true; }
Check(targetlessWithdrawalRejected, "targetless release withdrawal was admitted");
using var approvalKeyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var approvalPublicKey = new ReleaseApprovalKey("release-operator", approvalKeyPair.ExportSubjectPublicKeyInfo(),
    start.AddMinutes(-1), start.AddHours(2), [ReleaseAuthorizationAction.Publish, ReleaseAuthorizationAction.Withdraw]);
var publishedForAuthorization = releaseManifest with { Lifecycle = ReleaseManifestLifecycle.Published };
var approval = ReleaseApprovalSigner.Sign(publishedForAuthorization, ReleaseAuthorizationAction.Publish,
    approvalPublicKey.ApproverId, "release-policy-1", start, start.AddHours(1), approvalKeyPair);
var approvalPolicy = new ReleaseAuthorizationPolicy("release-policy-1", 1,
    new HashSet<string>([approvalPublicKey.KeyId], StringComparer.Ordinal));
ReleaseAuthorizationContext ApprovalContext(params ReleaseApproval[] approvals) => new(approvals,
    new Dictionary<string, ReleaseApprovalKey>(StringComparer.Ordinal) { [approvalPublicKey.KeyId] = approvalPublicKey },
    approvalPolicy, start.AddMinutes(1));
var validAuthorization = ReleaseAuthorizationValidator.Validate(publishedForAuthorization, ApprovalContext(approval));
var missingAuthorization = ReleaseAuthorizationValidator.Validate(publishedForAuthorization, ApprovalContext());
var wrongManifestAuthorization = ReleaseAuthorizationValidator.Validate(
    publishedForAuthorization with { SourceRevision = "different-source" }, ApprovalContext(approval));
var tamperedSignatureBytes = Convert.FromBase64String(approval.Signature); tamperedSignatureBytes[0] ^= 1;
var tamperedAuthorization = ReleaseAuthorizationValidator.Validate(publishedForAuthorization,
    ApprovalContext(approval with { Signature = Convert.ToBase64String(tamperedSignatureBytes) }));
var expiredContext = ApprovalContext(approval with { ExpiresAtUtc = start.AddSeconds(1) }) with
    { EvaluatedAtUtc = start.AddMinutes(1) };
var expiredAuthorization = ReleaseAuthorizationValidator.Validate(publishedForAuthorization, expiredContext);
var candidateAuthorization = ReleaseAuthorizationValidator.Validate(releaseManifest, ApprovalContext(approval));
Check(validAuthorization.Count == 0 && missingAuthorization.Contains("release-approval-threshold-not-met") &&
    wrongManifestAuthorization.Contains("release-approval-envelope-invalid") &&
    tamperedAuthorization.Contains("release-approval-signature-invalid") &&
    expiredAuthorization.Contains("release-approval-envelope-invalid") &&
    candidateAuthorization.Contains("candidate-manifest-has-release-approval"),
    "release authorization admitted missing, stale, tampered, expired, or candidate approval evidence");
var approvalRoot = Path.Combine(Path.GetTempPath(), $"hpd-payments-release-approval-{Environment.ProcessId}");
if (Directory.Exists(approvalRoot)) Directory.Delete(approvalRoot, recursive: true);
try
{
    var approvalWrites = await Task.WhenAll(Enumerable.Range(0, 16)
        .Select(_ => Task.Run(() => ReleaseApprovalStore.Write(approvalRoot, approval)))).ConfigureAwait(false);
    var loadedApproval = ReleaseApprovalStore.Load(approvalRoot, approval.ContentAddress());
    var loadedApprovals = ReleaseApprovalRepository.LoadAll(approvalRoot);
    Check(approvalWrites.Count(static write => write.Created) == 1 &&
        approvalWrites.Select(static write => write.Path).Distinct(StringComparer.Ordinal).Count() == 1 &&
        loadedApprovals.Count == 1 && loadedApproval.ToCanonicalText() == approval.ToCanonicalText() &&
        !Directory.EnumerateFiles(approvalRoot, "*.tmp", SearchOption.AllDirectories).Any() &&
        !Directory.EnumerateFiles(approvalRoot, "*.lock", SearchOption.AllDirectories).Any(),
        "concurrent release approval writes did not converge to one exact reloadable artifact");
    await File.AppendAllTextAsync(approvalWrites[0].Path, "1:x").ConfigureAwait(false);
    var tamperedApprovalRejected = false;
    try { _ = ReleaseApprovalStore.Load(approvalRoot, approval.ContentAddress()); }
    catch (InvalidDataException) { tamperedApprovalRejected = true; }
    Check(tamperedApprovalRejected, "tampered persisted release approval was admitted");
}
finally { if (Directory.Exists(approvalRoot)) Directory.Delete(approvalRoot, recursive: true); }

var publishedLineage = publishedForAuthorization with { PredecessorManifestDigest = releaseManifest.ContentAddress() };
var publishedLineageApproval = ReleaseApprovalSigner.Sign(publishedLineage, ReleaseAuthorizationAction.Publish,
    approvalPublicKey.ApproverId, approvalPolicy.Revision, start, start.AddHours(1), approvalKeyPair);
var withdrawalLineage = releaseManifest with { CreatedAtUtc = start.AddMinutes(2),
    PredecessorManifestDigest = publishedLineage.ContentAddress(),
    SupersedesManifestDigest = publishedLineage.ContentAddress(), Lifecycle = ReleaseManifestLifecycle.Withdrawal };
var withdrawalApproval = ReleaseApprovalSigner.Sign(withdrawalLineage, ReleaseAuthorizationAction.Withdraw,
    approvalPublicKey.ApproverId, approvalPolicy.Revision, start.AddMinutes(2), start.AddHours(1), approvalKeyPair);
var approvalKeys = new Dictionary<string, ReleaseApprovalKey>(StringComparer.Ordinal)
    { [approvalPublicKey.KeyId] = approvalPublicKey };
var validLineageAuthorization = ReleaseApprovalRepository.ValidateLineage(
    [releaseManifest, publishedLineage, withdrawalLineage], [publishedLineageApproval, withdrawalApproval],
    approvalKeys, approvalPolicy, start.AddMinutes(3));
var missingHistoricalAuthorization = ReleaseApprovalRepository.ValidateLineage(
    [releaseManifest, publishedLineage, withdrawalLineage], [withdrawalApproval], approvalKeys, approvalPolicy,
    start.AddMinutes(3));
var orphanApproval = withdrawalApproval with { ManifestAddress = new string('d', 64) };
var orphanAuthorization = ReleaseApprovalRepository.ValidateLineage(
    [releaseManifest, publishedLineage, withdrawalLineage], [publishedLineageApproval, withdrawalApproval, orphanApproval],
    approvalKeys, approvalPolicy, start.AddMinutes(3));
Check(validLineageAuthorization.Count == 0 &&
    missingHistoricalAuthorization.Contains("release-approval-threshold-not-met") &&
    orphanAuthorization.Contains("release-approval-orphan-manifest"),
    "release authorization lineage admitted a missing historical or orphan approval");
var manifestRoot = Path.Combine(Path.GetTempPath(), $"hpd-payments-release-manifest-{Environment.ProcessId}");
if (Directory.Exists(manifestRoot)) Directory.Delete(manifestRoot, recursive: true);
try
{
    var manifestWrites = await Task.WhenAll(Enumerable.Range(0, 16)
        .Select(_ => Task.Run(() => ReleaseManifestStore.Write(manifestRoot, releaseManifest)))).ConfigureAwait(false);
    var loadedManifest = ReleaseManifestStore.Load(manifestRoot, releaseManifest.ContentAddress());
    Check(manifestWrites.Count(static x => x.Created) == 1 &&
        manifestWrites.Select(static x => x.Path).Distinct(StringComparer.Ordinal).Count() == 1 &&
        !Directory.EnumerateFiles(manifestRoot, "*.tmp", SearchOption.AllDirectories).Any() &&
        !Directory.EnumerateFiles(manifestRoot, "*.lock", SearchOption.AllDirectories).Any() &&
        loadedManifest.ToCanonicalText() == releaseManifest.ToCanonicalText(),
        "concurrent release-manifest writes did not converge to one exact reloadable artifact");
    var manifestPath = manifestWrites[0].Path;
    await File.AppendAllTextAsync(manifestPath, "1:x").ConfigureAwait(false);
    var modifiedManifestRejected = false;
    try { _ = ReleaseManifestStore.Load(manifestRoot, releaseManifest.ContentAddress()); }
    catch (InvalidDataException) { modifiedManifestRejected = true; }
    Check(modifiedManifestRejected, "modified content-addressed release manifest was admitted");
}
finally
{
    if (Directory.Exists(manifestRoot)) Directory.Delete(manifestRoot, recursive: true);
}
Check(!Directory.Exists(manifestRoot), "release-manifest fixture cleanup failed");
var manifestChainRoot = Path.Combine(Path.GetTempPath(), $"hpd-payments-release-chain-{Environment.ProcessId}");
if (Directory.Exists(manifestChainRoot)) Directory.Delete(manifestChainRoot, recursive: true);
try
{
    var firstManifestWrite = ReleaseManifestStore.Write(manifestChainRoot, releaseManifest);
    var secondManifest = releaseManifest with { SourceRevision = "synthetic-fixture-2",
        CreatedAtUtc = start.AddSeconds(1), PredecessorManifestDigest = firstManifestWrite.ContentAddress };
    _ = ReleaseManifestStore.Write(manifestChainRoot, secondManifest);
    var manifestChain = ReleaseManifestRepository.LoadChain(manifestChainRoot, snapshot);
    Check(manifestChain.Count == 2 && manifestChain[0].ContentAddress() == firstManifestWrite.ContentAddress &&
        manifestChain[1].ContentAddress() == secondManifest.ContentAddress(),
        "unordered release manifests did not reconstruct one exact lineage");
    var forkedManifest = releaseManifest with { SourceRevision = "synthetic-fork",
        CreatedAtUtc = start.AddSeconds(2), PredecessorManifestDigest = firstManifestWrite.ContentAddress };
    _ = ReleaseManifestStore.Write(manifestChainRoot, forkedManifest);
    var manifestForkRejected = false;
    try { _ = ReleaseManifestRepository.LoadChain(manifestChainRoot, snapshot); }
    catch (InvalidDataException) { manifestForkRejected = true; }
    Check(manifestForkRejected, "forked release-manifest lineage was admitted");
}
finally
{
    if (Directory.Exists(manifestChainRoot)) Directory.Delete(manifestChainRoot, recursive: true);
}
var malformedSelection = currentDispositions.ToArray();
malformedSelection[0] = new(malformedSelection[0].CanonicalId, RouteDispositionKind.Selected, "malformed wildcard attack",
    [cell with { CanonicalId = malformedSelection[0].CanonicalId, Profile = "*" }]);
var malformedResult = ReleaseSelectionValidator.Validate(snapshot, malformedSelection);
Check(!malformedResult.InventoryValid && malformedResult.Errors.Contains("non-concrete-selected-cell"),
    "wildcard selected release cell was admitted");
var inventedBlocked = currentDispositions.ToArray();
inventedBlocked[0] = inventedBlocked[0] with { Kind = RouteDispositionKind.Blocked };
Check(ReleaseSelectionValidator.Validate(snapshot, inventedBlocked).Errors.Contains("invented-route-block"),
    "accepted current route was reblocked by release selection");
var unsupportedWithoutEvidence = currentDispositions.ToArray();
var unsupportedIndex = Array.FindIndex(unsupportedWithoutEvidence, static x => x.Kind == RouteDispositionKind.Untested);
unsupportedWithoutEvidence[unsupportedIndex] = unsupportedWithoutEvidence[unsupportedIndex] with
    { Kind = RouteDispositionKind.Unsupported };
Check(ReleaseSelectionValidator.Validate(snapshot, unsupportedWithoutEvidence).Errors
    .Contains("unsupported-without-negative-evidence"), "unsupported route without exact negative evidence passed");
var unsupportedRoute = snapshot.Routes.Single(x => x.Id == unsupportedWithoutEvidence[unsupportedIndex].CanonicalId);
var unsupportedCell = cell with { CanonicalId = unsupportedRoute.Id,
    Owner = unsupportedRoute.AuthorityOwners.Count == 0 ? unsupportedRoute.OwnerOrSupportingConcept : unsupportedRoute.AuthorityOwners[0],
    Family = unsupportedRoute.CandidateContractFamily, Workload = "negative-capability-probe" };
var negativeReceipt = Make(selectedCell: unsupportedCell) with { ReceiptId = "negative-receipt", RunId = "negative-run" };
var unsupportedWithEvidence = currentDispositions.ToArray();
unsupportedWithEvidence[unsupportedIndex] = new(unsupportedCell.CanonicalId, RouteDispositionKind.Unsupported,
    "synthetic executed negative capability evidence", Array.Empty<ProofCellKey>())
    { EvidenceReceiptDigests = [negativeReceipt.ContentAddress()] };
var negativeEvidenceResult = ReleaseEvidenceValidator.Validate(snapshot, unsupportedWithEvidence, [negativeReceipt]);
Check(negativeEvidenceResult.Selection.InventoryValid && negativeEvidenceResult.DispositionEvidenceComplete &&
    !negativeEvidenceResult.ReleaseReady, "exact negative evidence did not join or falsely completed the release");
var missingNegativeEvidence = ReleaseEvidenceValidator.Validate(snapshot, unsupportedWithEvidence, []);
Check(!missingNegativeEvidence.DispositionEvidenceComplete &&
    missingNegativeEvidence.EvidenceErrors.Contains("missing-disposition-evidence"),
    "missing negative evidence receipt passed the release join");
var selectedDispositions = currentDispositions.ToArray();
var selectedIndex = Array.FindIndex(selectedDispositions, static x => x.Kind == RouteDispositionKind.Untested);
var selectedRoute = snapshot.Routes.Single(x => x.Id == selectedDispositions[selectedIndex].CanonicalId);
var selectedCell = cell with { CanonicalId = selectedRoute.Id,
    Owner = selectedRoute.AuthorityOwners.Count == 0 ? selectedRoute.OwnerOrSupportingConcept : selectedRoute.AuthorityOwners[0],
    Family = selectedRoute.CandidateContractFamily };
selectedDispositions[selectedIndex] = new(selectedCell.CanonicalId, RouteDispositionKind.Selected,
    "synthetic exact-cell join fixture", [selectedCell]);
var missingSelectedReceipt = ReleaseEvidenceValidator.Validate(snapshot, selectedDispositions, []);
Check(!missingSelectedReceipt.SelectedEvidenceComplete && missingSelectedReceipt.Proof.Errors.Contains("missing-cell"),
    "selected cell without a receipt passed the joined release gate");
var joinedSelectedReceipt = ReleaseEvidenceValidator.Validate(snapshot, selectedDispositions, [Make(selectedCell: selectedCell)]);
Check(joinedSelectedReceipt.SelectedEvidenceComplete && !joinedSelectedReceipt.ReleaseReady,
    $"exact selected receipt did not join, or incomplete inventory was falsely release-ready: selection={string.Join(',', joinedSelectedReceipt.Selection.Errors)} proof={string.Join(',', joinedSelectedReceipt.Proof.Errors)} ready={joinedSelectedReceipt.ReleaseReady}");
var joinedManifestRoot = Path.Combine(Path.GetTempPath(), $"hpd-payments-joined-manifest-{Environment.ProcessId}");
var joinedReceiptRoot = Path.Combine(Path.GetTempPath(), $"hpd-payments-joined-receipt-{Environment.ProcessId}");
if (Directory.Exists(joinedManifestRoot)) Directory.Delete(joinedManifestRoot, recursive: true);
if (Directory.Exists(joinedReceiptRoot)) Directory.Delete(joinedReceiptRoot, recursive: true);
try
{
    var joinedManifest = releaseManifest with { SourceRevision = "rev", Dispositions = selectedDispositions };
    var manifestWrite = ReleaseManifestStore.Write(joinedManifestRoot, joinedManifest);
    _ = ProofReceiptStore.Write(joinedReceiptRoot, Make(selectedCell: selectedCell));
    var durableJoin = ReleaseRepository.Validate(snapshot, joinedManifestRoot, manifestWrite.ContentAddress, joinedReceiptRoot);
    Check(durableJoin.Errors.Count == 0 && durableJoin.Evidence.SelectedEvidenceComplete &&
        durableJoin.Manifest.Lifecycle == ReleaseManifestLifecycle.Candidate && !durableJoin.ReleaseReady,
        "durable manifest/receipt repositories did not join exact selected evidence honestly");
    var staleManifest = joinedManifest with { SourceRevision = "stale-revision", CreatedAtUtc = start.AddSeconds(1),
        PredecessorManifestDigest = manifestWrite.ContentAddress };
    _ = ReleaseManifestStore.Write(joinedManifestRoot, staleManifest);
    var staleJoin = ReleaseRepository.ValidateCurrent(snapshot, joinedManifestRoot, joinedReceiptRoot);
    Check(staleJoin.Errors.Contains("selected-receipt-source-revision-mismatch") && !staleJoin.ReleaseReady,
        "stale selected receipt was admitted by the durable release repository");
}
finally
{
    if (Directory.Exists(joinedManifestRoot)) Directory.Delete(joinedManifestRoot, recursive: true);
    if (Directory.Exists(joinedReceiptRoot)) Directory.Delete(joinedReceiptRoot, recursive: true);
}
var ownerSubstitution = selectedDispositions.ToArray();
ownerSubstitution[selectedIndex] = ownerSubstitution[selectedIndex] with
    { SelectedCells = [selectedCell with { Owner = "wrong-owner" }] };
Check(ReleaseSelectionValidator.Validate(snapshot, ownerSubstitution).Errors.Contains("selected-cell-owner-mismatch"),
    "count-preserving authority-owner substitution passed release selection");
var upstream = Make() with { ReceiptId = "receipt-upstream" };
var dependentCell = cell with { Workload = "dependent-workload" };
var dependent = Make(upstream.ContentAddress(), selectedCell: dependentCell) with
    { ReceiptId = "receipt-dependent", DependencyDigests = [upstream.ContentAddress()] };
var invalidation = Make(dependent.ContentAddress(), ProofState.Inspected) with
    { ReceiptId = "receipt-invalidation", InvalidatesDigest = upstream.ContentAddress(), Lifecycle = ReceiptLifecycle.Invalidation };
var invalidationResult = ProofLedgerValidator.Validate([upstream, dependent, invalidation], [cell, dependentCell],
    CanonicalRegistryDigest, ClaimMatrixDigest);
Check(invalidationResult.Errors.Contains("stale-dependent-receipt"),
    "upstream invalidation did not transitively stale a dependent current receipt");

if (failures.Count != 0) { foreach (var failure in failures) await Console.Error.WriteLineAsync(failure).ConfigureAwait(false); return 1; }
await Console.Out.WriteLineAsync("PASS provisional proof core: attacks, deterministic exploration/store, H0-H13 and 48 H7 coordinates verified").ConfigureAwait(false);
return 0;
