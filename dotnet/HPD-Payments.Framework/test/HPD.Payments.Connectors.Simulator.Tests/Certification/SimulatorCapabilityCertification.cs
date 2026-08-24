using HPD.Payments.Connectors.Simulator.Core;
using HPD.Payments.Connectors.Simulator.Scenarios;
using HPD.Payments.Contracts.CapabilityEvidence;
using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Connectors.Simulator.Tests.Certification;

internal static class SimulatorCapabilityCertification
{
    private static readonly DateTimeOffset Epoch = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static void RunAll()
    {
        ScopeId scope = ScopeId.Create("tenant", "certification", "simulator");
        SemanticId account = SemanticId.Create(scope, "connector", "account", "deterministic", "simulator", "local");
        var context = new CapabilityContext(account, "capture", "simulator-v1", Revision.Create("code", 1),
            Revision.Create("configuration", 1), Revision.Create("credential", 1), "static", "osx-arm64");
        var profile = new CanonicalDigestProfileId("simulator-capability", ContractVersion.Create(1, 0),
            "context-disposition", "ordinal", "utc", "ordered", "none");
        CanonicalDigest Digest(string value) => CanonicalDigest.Sha256(profile, System.Text.Encoding.UTF8.GetBytes(value));
        SemanticId Evidence(string local) => SemanticId.Create(scope, "connector", "evidence", local);
        NamedTime verified = NamedTime.Create(TimeKind.Verify, Epoch);
        NamedTime expiry = NamedTime.Create(TimeKind.Expiry, Epoch.AddHours(1));
        var positive = new CapabilityEvidenceFact(Evidence("positive"), context, CapabilityDisposition.Positive,
            "deterministic-fixture", verified, expiry, Digest("positive"));
        Assert(positive.EstablishesSupport(Epoch.AddMinutes(1)), "current positive simulator evidence did not establish support");
        foreach (CapabilityDisposition disposition in new[] { CapabilityDisposition.Negative, CapabilityDisposition.Conditional })
            Assert(!new CapabilityEvidenceFact(Evidence($"nonpositive-{(int)disposition}"), context, disposition,
                "explicit-nonpositive", verified, expiry, Digest(disposition.ToString())).EstablishesSupport(Epoch.AddMinutes(1)),
                $"{disposition} simulator evidence established support");
        foreach (CapabilityDisposition disposition in new[] { CapabilityDisposition.Expired, CapabilityDisposition.Withdrawn, CapabilityDisposition.Conflicted })
            Assert(!new CapabilityEvidenceFact(Evidence($"lineage-{(int)disposition}"), context, disposition,
                "lineage-nonpositive", verified, expiry, Digest(disposition.ToString()), positive.EvidenceId)
                .EstablishesSupport(Epoch.AddMinutes(1)), $"{disposition} simulator evidence established support");
        Assert(!positive.EstablishesSupport(Epoch.AddHours(2)), "expired positive simulator evidence remained current");

        var engine = new SimulatorEngine(Revision.Create("credential", 1), Revision.Create("configuration", 1));
        var request = new SimulatorRequest("capture-certification", Revision.Create("credential", 1), Revision.Create("configuration", 1));
        SimulatorResult uncertain = engine.Execute(request, BootstrapScenarios.PossibleDispatch(), new(Epoch));
        Assert(uncertain.State == ExternalEffectState.PossibleDispatch, "simulator certification flattened dispatch ambiguity");
        SimulatorResult mismatch = engine.Execute(request, BootstrapScenarios.SettlementDisagreement(), new(Epoch));
        Assert(mismatch.State == ExternalEffectState.ConfirmedOccurred && mismatch.SettlementState == SimulatorSettlementState.NotIncluded &&
            mismatch.HasCrossAuthorityMismatch, "simulator certification collapsed occurrence and settlement questions");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
