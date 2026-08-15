using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.ExternalEffects;

namespace HPD.Payments.Runtime.Tests.ExternalEffects;

internal static class ExternalEffectProtocolProofs
{
    internal static void Run(List<string> failures)
    {
        void Check(bool value, string message) { if (!value) failures.Add(message); }
        var scope = ScopeId.Create("tenant", "runtime", "effect");
        SemanticId Id(string kind, string local, string? provider = null, string? account = null) =>
            SemanticId.Create(scope, "runtime", kind, local, provider, account);
        var profile = new CanonicalDigestProfileId("runtime", ContractVersion.Create(1, 0), "fields", "ordinal", "utc", "ordered", "none");
        CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));
        var operation = new ExternalEffectOperation(Id("operation", "capture"), Id("attempt", "one"),
            Id("account", "stripe-main", "stripe", "acct-main"), "idem-1", Digest("request"),
            Revision.Create("credential", 1), Revision.Create("configuration", 1));

        var initial = ExternalEffectProtocolState.Create(operation, Digest("initial"));
        var dispatching = initial.BeginDispatch(Digest("dispatching"));
        var possible = dispatching.State.MarkPossibleDispatch(Digest("possible"));
        Check(possible.State.RequiresResolution && !possible.State.PermitsDispatch, "possible dispatch permitted blind retry");
        Check(!possible.State.BeginDispatch(Digest("retry")).Accepted, "possible dispatch crossed another send boundary");
        var occurred = possible.State.Synchronize(true, Digest("provider-observed"));
        Check(occurred.State.State == ExternalEffectState.ConfirmedOccurred && !occurred.State.PermitsDispatch,
            "confirmed occurrence permitted retry");

        var second = ExternalEffectProtocolState.Create(operation, Digest("initial-2")).BeginDispatch(Digest("dispatch-2"));
        var notOccurred = second.State.Synchronize(false, Digest("verified-absent"));
        Check(notOccurred.State.PermitsDispatch, "confirmed non-occurrence did not permit governed retry");
        var adjudicated = occurred.State.Adjudicate(Digest("decision"));
        Check(adjudicated.Accepted && adjudicated.State.State == ExternalEffectState.Adjudicated, "governed adjudication failed");
    }
}
