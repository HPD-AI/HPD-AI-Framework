using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.DurableWork;
using HPD.Payments.Worker;

if (!args.Contains("--self-test", StringComparer.Ordinal))
{
    Console.Error.WriteLine("HPD.Payments Worker requires an explicit durable-work activation envelope.");
    return 64;
}
var scope = ScopeId.Create("tenant", "worker", "self-test");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "worker", kind, local);
var digest = CanonicalDigest.Sha256(new("worker", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none"), "payload"u8);
var requirement = new WorkRequirementFact(Id("work", "one"), Id("fact", "owner"), digest, ContractVersion.Create(1, 0),
    Revision.Create("deployment", 1), NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 2);
var begun = PaymentsWorkerKernel.Begin(WorkProtocolState.Create(requirement), "worker-one",
    NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(10)));
var expired = PaymentsWorkerKernel.RecoverExpired(begun.State, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch.AddSeconds(10)));
if (!begun.Accepted || !expired.Accepted || !expired.State.RequiresReconciliation) return 1;
Console.WriteLine("PASS Worker self-test: claim epoch and crash-expiry reconciliation");
return 0;
