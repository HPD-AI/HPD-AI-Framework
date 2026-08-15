using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Admission;
using HPD.Payments.Runtime.Authorization;
using HPD.Payments.Runtime.CompositionContracts;
using HPD.Payments.Runtime.Tests.DurableWork;
using HPD.Payments.Runtime.Tests.ExternalEffects;
using HPD.Payments.Runtime.Tests.Publication;
using HPD.Payments.Runtime.Tests.Repair;
using HPD.Payments.Runtime.Tests.Custody;
using HPD.Payments.Runtime.Tests.History;
using HPD.Payments.Runtime.Orchestration;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Runtime.Tests.Orchestration;

/// <summary>Executes the adapter-neutral runtime baseline fixture.</summary>
public static class RuntimeBaselineFixture
{
/// <summary>Runs current-action, fake-port, dependency, and closed-registration checks.</summary>
/// <returns>Zero when every check passes; otherwise one.</returns>
public static async Task<int> Main()
{
var failures = new List<string>();
void Check(bool condition, string message) { if (!condition) failures.Add(message); }
WorkProtocolProofs.Run(failures);
PublicationProtocolProofs.Run(failures);
ExternalEffectProtocolProofs.Run(failures);
GovernedRepairProtocolProofs.Run(failures);
CustodyProtocolProofs.Run(failures);
ProviderEvidencePrecedenceProofs.Run(failures);
RuntimeHistoryTraceProofs.Run(failures);

var scope = ScopeId.Create("tenant", "live", "work");
SemanticId Id(string kind, string local) => SemanticId.Create(scope, "runtime", kind, local);
var subject = Id("work", "w1");
var owner = new OwnerReference(FrozenAuthority.WorkRequirement, subject, OwnerGeneration.Create(1));
var profile = new CanonicalDigestProfileId("runtime", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
var digest = CanonicalDigest.Sha256(profile, "fact"u8);
var domain = new AtomicDomain(Id("domain", "local"), AtomicDomainKind.Local, Revision.Create("topology", 1));
var fact = new WorkRequirementFact(subject, Id("fact", "source"), digest, ContractVersion.Create(1, 0),
    Revision.Create("deployment", 1), NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 3);
var policyRevision = Revision.Create("policy", 7);
var authorization = new CurrentActionRequest<string>(Id("principal", "operator"), subject, policyRevision,
    NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), "admit-work");
var request = new AdmissionRequest<string, WorkRequirementFact>(authorization, new OwnerAppendRequest<WorkRequirementFact>(owner, digest, domain, fact));

var allowedPort = new FakePort();
var allowed = await new OwnerFactOrchestrator<string, WorkRequirementFact>(new FakeAuthorizer(AuthorizationDisposition.Authorized, policyRevision), allowedPort)
    .AdmitAsync(request).ConfigureAwait(false);
Check(allowed.Disposition == AdmissionDisposition.Attempted && allowed.Persistence?.Disposition == OwnerAppendDisposition.Appended,
    "Authorized orchestration did not preserve the typed persistence receipt.");
Check(allowedPort.Calls == 1, "Authorized orchestration did not invoke persistence exactly once.");

var deniedPort = new FakePort();
var denied = await new OwnerFactOrchestrator<string, WorkRequirementFact>(new FakeAuthorizer(AuthorizationDisposition.Denied, policyRevision), deniedPort)
    .AdmitAsync(request).ConfigureAwait(false);
Check(denied.Disposition == AdmissionDisposition.Denied && denied.Persistence is null && deniedPort.Calls == 0,
    "Denied current action reached persistence.");

var stalePort = new FakePort();
var stale = await new OwnerFactOrchestrator<string, WorkRequirementFact>(new FakeAuthorizer(AuthorizationDisposition.Authorized, Revision.Create("policy", 8)), stalePort)
    .AdmitAsync(request).ConfigureAwait(false);
Check(stale.Disposition == AdmissionDisposition.Indeterminate && stale.Persistence is null && stalePort.Calls == 0,
    "Stale policy authorization reached persistence.");

var runtimeReferences = typeof(OwnerFactOrchestrator<,>).Assembly.GetReferencedAssemblies().Select(static x => x.Name).ToHashSet(StringComparer.Ordinal);
Check(runtimeReferences.Contains("HPD.Payments.Persistence"), "Runtime does not depend inward on Persistence.");
Check(!runtimeReferences.Any(static x => x is not null && (x.StartsWith("HPD.Payments.Adapters", StringComparison.Ordinal) ||
    x.StartsWith("HPD.Payments.Connectors", StringComparison.Ordinal) || x.StartsWith("HPD.Payments.Profiles", StringComparison.Ordinal))),
    "Runtime references an adapter, connector, or profile assembly.");

var registrationConstructor = typeof(RuntimePersistencePorts).GetConstructors().Single();
Check(registrationConstructor.GetParameters().Length == 17, "Closed runtime registration does not require all seventeen authority ports.");
Check(typeof(RuntimePersistencePorts).Assembly.GetTypes().All(static type =>
    !type.FullName!.Contains("Reflection", StringComparison.Ordinal) && !type.FullName.Contains("ServiceLocator", StringComparison.Ordinal)),
    "Runtime baseline exposes discovery or service-location infrastructure.");

if (failures.Count != 0)
{
    foreach (var failure in failures) await Console.Error.WriteLineAsync(failure).ConfigureAwait(false);
    return 1;
}

Console.WriteLine($"Runtime proofs passed: {17} closed ports, current-action admission, work/publication/effect/repair/custody protocols, provider precedence, 11 complete H0-H13 histories, fake-port orchestration, and inward dependencies.");
return 0;
}

sealed class FakeAuthorizer(AuthorizationDisposition disposition, Revision revision) : ICurrentActionAuthorizer<string>
{
    public ValueTask<AuthorizationDecision> AuthorizeAsync(CurrentActionRequest<string> request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AuthorizationDecision(disposition, revision, disposition switch
        {
            AuthorizationDisposition.Authorized => "authorized",
            AuthorizationDisposition.Denied => "denied",
            _ => "indeterminate",
        }));
}

sealed class FakePort : IOwnerPersistencePort<WorkRequirementFact>
{
    public int Calls { get; private set; }

    public ValueTask<OwnerAppendReceipt<WorkRequirementFact>> CompareBindAppendAsync(OwnerAppendRequest<WorkRequirementFact> request, CancellationToken cancellationToken = default)
    {
        Calls++;
        return ValueTask.FromResult(new OwnerAppendReceipt<WorkRequirementFact>(request.ExpectedOwner, OwnerAppendDisposition.Appended,
            request.ExpectedOwner.Generation, request.Fact, "appended"));
    }

    public ValueTask<OwnerHistoryPage<WorkRequirementFact>> ReadHistoryAsync(OwnerHistoryRequest request, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("History is outside this focused orchestration fixture.");
}
}
