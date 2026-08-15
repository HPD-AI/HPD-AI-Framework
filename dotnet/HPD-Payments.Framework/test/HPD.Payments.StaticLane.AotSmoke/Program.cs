using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Runtime.Custody;
using HPD.Payments.Runtime.DurableWork;
using HPD.Payments.Runtime.ExternalEffects;
using HPD.Payments.Runtime.Publication;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Ownership;

var scope = ScopeId.Create("tenant", "aot", "static-lane");
SemanticId Id(string kind, string local, string? provider = null, string? account = null) =>
    SemanticId.Create(scope, "static", kind, local, provider, account);
var version = ContractVersion.Create(1, 0);
var profile = new CanonicalDigestProfileId("static", version, "fields", "ordinal", "utc", "ordered", "none");
CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));

var requirement = new WorkRequirementFact(Id("work", "one"), Id("fact", "owner"), Digest("work"), version,
    Revision.Create("deployment", 1), NamedTime.Create(TimeKind.Requested, DateTimeOffset.UnixEpoch), 2);
var claimed = WorkProtocolState.Create(requirement).TryClaim("static-worker",
    NamedTime.Create(TimeKind.Expiry, DateTimeOffset.UnixEpoch.AddMinutes(1)));
var verified = claimed.State.Observe(claimed.State.ClaimEpoch, WorkAttemptObservation.OwnerPostconditionVerified);

var obligation = new PublicationObligationFact(Id("publication", "one"), Id("fact", "source"), "merchant",
    "events", Digest("publication"), version, NamedTime.Create(TimeKind.Record, DateTimeOffset.UnixEpoch));
var dispatched = PublicationProtocolState.Create(obligation).Dispatch(Id("delivery", "one"));
var acknowledged = dispatched.State.Acknowledge(Id("delivery", "one"));

var operation = new ExternalEffectOperation(Id("operation", "capture"), Id("attempt", "one"),
    Id("account", "main", "simulator", "main"), "static-idempotency", Digest("request"),
    Revision.Create("credential", 1), Revision.Create("configuration", 1));
var effect = ExternalEffectProtocolState.Create(operation, Digest("initial"))
    .BeginDispatch(Digest("dispatch")).State.MarkPossibleDispatch(Digest("possible"));

var owner = new OwnerReference(FrozenAuthority.Obligation, Id("obligation", "one"), OwnerGeneration.Create(1));
var custody = CustodyProtocol.Create(new CustodyInstance(Id("instance", "one"), owner, Id("controller", "one"),
    OwnerGeneration.Create(1), ClassificationMark.Create(DataClassification.Confidential, RetentionKind.Durable),
    Revision.Create("policy", 1), Revision.Create("hold", 1), CustodyState.Eligible,
    NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch)));

if (verified.State.Disposition != WorkDisposition.Verified ||
    acknowledged.State.Disposition != PublicationDisposition.Acknowledged ||
    effect.State.State != ExternalEffectState.PossibleDispatch || !custody.Current.InstanceId.IsValid)
    return 1;

var message = "PASS static lane closed graph: work/publication/effect/custody rooted without reflection";
await Console.Out.WriteLineAsync(message).ConfigureAwait(false);
return 0;
