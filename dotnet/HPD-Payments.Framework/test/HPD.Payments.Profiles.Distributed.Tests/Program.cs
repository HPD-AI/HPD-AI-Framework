using HPD.Payments.Primitives.Identity;
using HPD.Payments.Profiles.Distributed;

var scope = ScopeId.Create("tenant", "profile", "cutover");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "profile", kind, local);
var profile = new CanonicalDigestProfileId("cutover", ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless");
CutoverHistoryEntry Entry(ulong generation, string value) => new(Id("owner", "one"), OwnerGeneration.Create(generation),
    CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value)));
var source = new[] { Entry(1, "one"), Entry(2, "two") };
var target = new[] { Entry(1, "one"), Entry(2, "two") };
var planned = DistributedCutoverProtocol.Plan(Id("cutover", "one"), Id("source", "embedded"), Id("target", "distributed"));
var dual = planned.BeginDualRead(source, target, OwnerGeneration.Create(1));
Require(dual.State == DistributedCutoverState.DualRead, "identical dual-read history rejected");
var promoted = dual.Promote(source, target, OwnerGeneration.Create(2), TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10));
Require(promoted.State == DistributedCutoverState.TargetPrimary, "identical bounded-skew promotion rejected");
Require(promoted.Complete(source, target, OwnerGeneration.Create(2), false).State == DistributedCutoverState.Completed,
    "identical replay did not complete");
Require(promoted.Rollback(source, target, OwnerGeneration.Create(2)).State == DistributedCutoverState.RolledBack,
    "identical rollback history rejected");
var divergent = new[] { Entry(1, "one"), Entry(2, "different") };
Require(dual.Promote(source, divergent, OwnerGeneration.Create(2), TimeSpan.Zero, TimeSpan.FromSeconds(1)).State == DistributedCutoverState.Residual,
    "divergent promotion admitted");
Require(dual.Promote(source, target, OwnerGeneration.Create(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)).ResidueCode == "promotion-clock-skew",
    "excessive skew admitted");
Require(promoted.Complete(source, target, OwnerGeneration.Create(2), true).ResidueCode == "completion-residue",
    "named residue was discarded");
Console.WriteLine("PASS distributed cutover: dual-read, promotion, replay, rollback, divergence, skew, and residue");
return 0;

static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
