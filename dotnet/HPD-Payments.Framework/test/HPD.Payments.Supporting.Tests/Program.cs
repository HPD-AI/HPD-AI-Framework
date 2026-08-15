using System.Reflection;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Evidence;
using HPD.Payments.Supporting.History;
using HPD.Payments.Supporting.Operations;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;
using HPD.Payments.Supporting.Repair;

var failures = new List<string>();
void Check(bool condition, string message) { if (!condition) failures.Add(message); }
void Reject(Action action, string message) { try { action(); failures.Add(message); } catch (ArgumentException) { } }

var scope = ScopeId.Create("tenant", "live", "obligation");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "support", kind, local);
var generation = OwnerGeneration.Create(1);
var owner = new OwnerReference(FrozenAuthority.Obligation, Id("obligation", "o1"), generation);
var movement = new OwnerReference(FrozenAuthority.ValueMovement, Id("movement", "m1"), generation);
var revision = Revision.Create("relation", 1);
var relation = new SupportingRelation(Id("relation", "r1"), SupportingRelationKind.Application, owner, movement, revision);
var application = new ApplicationRelation(relation, 10m, "usd");
Check(application.Relation.Source.Authority == FrozenAuthority.Obligation, "Application lost its frozen source owner.");
Check(application.Relation.Target.Authority == FrozenAuthority.ValueMovement, "Application lost its frozen target owner.");
Reject(() => _ = new ApplicationRelation(new SupportingRelation(Id("relation", "r2"), SupportingRelationKind.Match, owner, movement, revision), 1m, "usd"), "Non-Application relation was admitted as Application.");

var profile = new CanonicalDigestProfileId("supporting", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
var digest = CanonicalDigest.Sha256(profile, "evidence"u8);
var classification = ClassificationMark.Create(DataClassification.Restricted, RetentionKind.Durable);
var verifyAt = NamedTime.Create(TimeKind.Verify, DateTimeOffset.UnixEpoch);
var observedAt = NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch);
var expiry = NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddDays(1));
var evidence = new EvidenceDescriptor(Id("evidence", "e1"), EvidenceRole.Authorization, owner, Id("source", "s1"), digest, classification, observedAt);
var authorization = new AuthorizationDescriptor(evidence, "repair", Revision.Create("policy", 1), expiry);
var operation = new OperationDeclaration(Id("operation", "op1"), owner, digest, authorization);
var branch = new RepairBranch(Id("branch", "b1"), owner, operation, "verify-balance");
var governedCase = new GovernedCase(Id("case", "c1"), generation, new[] { branch });
Check(governedCase.Branches.Count == 1, "Governed Case did not retain its typed branch.");
Reject(() => _ = new GovernedCase(Id("case", "c2"), generation, new[] { branch, branch }), "Duplicate repair branch was admitted.");

var verification = new EvidenceDescriptor(Id("evidence", "v1"), EvidenceRole.Verification, owner, Id("source", "s2"), digest, classification, verifyAt);
var attempt = new OperationAttempt(operation, Id("attempt", "a1"), AttemptOutcome.Verified, NamedTime.Create(TimeKind.Dispatch, DateTimeOffset.UnixEpoch), verification);
Check(attempt.Outcome == AttemptOutcome.Verified, "Verified operation attempt was not preserved.");
Reject(() => _ = new OperationAttempt(operation, Id("attempt", "a2"), AttemptOutcome.Verified, NamedTime.Create(TimeKind.Dispatch, DateTimeOffset.UnixEpoch)), "Verified attempt omitted verification evidence.");
_ = new RepairBranchReceipt(branch.BranchId, RepairBranchTerminal.VerifiedPostcondition, verification);
Reject(() => _ = new RepairBranchReceipt(branch.BranchId, RepairBranchTerminal.Residual), "Residual terminal omitted its residue.");

var cut = new HPD.Payments.Supporting.History.OwnerCut(owner);
var frame = new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), new[] { cut });
var projection = new ProjectionDeclaration(Id("projection", "p1"), generation, frame, digest);
Check(projection.SourceFrame.OwnerCuts[0].Owner.Authority == FrozenAuthority.Obligation, "Projection source cut lost authority ownership.");
Reject(() => _ = new HistoricalFrame(HPD.Payments.Supporting.History.HistoricalFrameKind.AsKnownAt, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch), new[] { cut, cut }), "Duplicate owner cut was admitted.");

var custody = new CustodyInstance(Id("custody", "i1"), owner, Id("controller", "store1"), generation, classification,
    Revision.Create("retention", 1), Revision.Create("holds", 1), CustodyState.KnownPresent, observedAt);
Check(custody.State == CustodyState.KnownPresent, "Custody observation was flattened.");
var epoch = new ClaimEpoch(Id("claim", "cl1"), owner, Revision.Create("provider-api", 2), digest, expiry);
Check(epoch.Owner.Authority == FrozenAuthority.Obligation, "Claim epoch lost frozen authority routing.");

var assembly = typeof(GovernedCase).Assembly;
var exported = assembly.GetExportedTypes();
var forbiddenNames = new[] { "Repository", "EventEnvelope", "SetStatus", "Mutator", "SaveChanges", "Universal" };
foreach (var type in exported)
{
    Check(!forbiddenNames.Any(x => type.Name.Contains(x, StringComparison.OrdinalIgnoreCase)), $"Forbidden universal/mutation type surfaced: {type.FullName}");
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        Check(!forbiddenNames.Any(x => method.Name.Contains(x, StringComparison.OrdinalIgnoreCase)), $"Forbidden mutation/repository method surfaced: {type.FullName}.{method.Name}");
}
Check(exported.All(static x => !x.IsInterface), "Supporting exposed a service/repository interface that could own mutation truth.");

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return 1;
}

Console.WriteLine($"Supporting contracts passed: {exported.Length} public declarations; owner routing, negative construction, and no-authority surface verified.");
return 0;
