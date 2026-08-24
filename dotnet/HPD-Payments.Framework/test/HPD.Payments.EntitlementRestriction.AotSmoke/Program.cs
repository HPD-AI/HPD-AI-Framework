using HPD.Payments.Contracts.EntitlementGrantRemovalFact;
using HPD.Payments.Contracts.RestrictionFact;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Entitlement;

var scope = ScopeId.Create("tenant", "aot", "entitlement");
SemanticId Id(string ns, string kind, string local) => SemanticId.Create(scope, ns, kind, local);
NamedTime At(long seconds) => NamedTime.Create(TimeKind.Effective, DateTimeOffset.UnixEpoch.AddSeconds(seconds));
var subject = Id("subject", "account", "one"); var owner = Id("owner", "restriction", "collections");
var digest = CanonicalDigest.Sha256(new("entitlement", ContractVersion.Create(1, 0), "semantic", "none", "decimal-time-v1", "ordered", "sha256-keyless"), "premium"u8);
var state = EntitlementRestrictionState.Create(subject, OwnerGeneration.Create(1));
state = state.Apply(new EntitlementCommand(Id("entitlement", "fact", "grant"), subject, "premium", digest, Id("evidence", "agreement", "one"),
    EntitlementOperation.Grant, EntitlementPrecedence.Initial, state.Generation, At(10)), DateTimeOffset.UnixEpoch.AddSeconds(5));
var restriction = Id("restriction", "fact", "overdue");
state = state.Apply(new RestrictionCommand(restriction, subject, owner, "service-access", Id("evidence", "overdue", "one"),
    RestrictionOperation.Restrict, state.Generation, At(20)), DateTimeOffset.UnixEpoch.AddSeconds(15));
state = state.Apply(new RestrictionCommand(Id("restriction", "fact", "release"), subject, owner, "service-access", Id("evidence", "payment", "one"),
    RestrictionOperation.Release, state.Generation, At(30), predecessorFactId: restriction, predecessorOwnerId: owner), DateTimeOffset.UnixEpoch.AddSeconds(25));
if (state.Resolve("premium", "service-access", DateTimeOffset.UnixEpoch.AddSeconds(21), DateTimeOffset.UnixEpoch.AddSeconds(21), TimeSpan.FromSeconds(10), EnforcementFailMode.Closed).Kind != EnforcementDecisionKind.Deny ||
    state.Resolve("premium", "service-access", DateTimeOffset.UnixEpoch.AddSeconds(31), DateTimeOffset.UnixEpoch.AddSeconds(31), TimeSpan.FromSeconds(10), EnforcementFailMode.Closed).Kind != EnforcementDecisionKind.Allow)
    return 1;
Console.WriteLine("PASS entitlement/restriction Native AOT temporal graph");
return 0;
