using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.DurableWork;
using HPD.Payments.Worker;

if (!ReleaseCellBinding.ValidateAndExecute("test-worker")) return 1;
var scope = ScopeId.Create("tenant", "worker", "test");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "worker", kind, local);
var digest = CanonicalDigest.Sha256(new("worker", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none"), "payload"u8);
var requirement = new WorkRequirementFact(Id("work", "one"), Id("fact", "owner"), digest, ContractVersion.Create(1, 0), Revision.Create("deployment", 1),
    NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 2);
var begun = PaymentsWorkerKernel.Begin(WorkProtocolState.Create(requirement), "worker-a", NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(10)));
if (!begun.Accepted || PaymentsWorkerKernel.Complete(begun.State, OwnerGeneration.Create(2), WorkAttemptObservation.OwnerPostconditionVerified).Accepted) return 1;
var returned = PaymentsWorkerKernel.Complete(begun.State, begun.State.ClaimEpoch, WorkAttemptObservation.HandlerReturned);
if (!returned.State.RequiresReconciliation || PaymentsWorkerKernel.Begin(returned.State, "worker-b", NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(20))).Accepted) return 1;
var expired = PaymentsWorkerKernel.RecoverExpired(begun.State, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch.AddSeconds(10)));
if (!expired.State.RequiresReconciliation || expired.State.AttemptCount != 1) return 1;
var reconciled = expired.State.Reconcile(false).State;
var takeover = PaymentsWorkerKernel.Begin(reconciled, "worker-b", NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(20)));
if (!takeover.Accepted || takeover.State.ClaimEpoch.Value != 2) return 1;
Console.WriteLine("PASS Worker: stale epoch, handler return, crash expiry, reconciliation, and takeover fencing");
return 0;
