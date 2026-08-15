using HPD.Payments.Connectors.Simulator.Core;
using HPD.Payments.Connectors.Simulator.Scenarios;
using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Connectors.Simulator.Tests.Baseline;

/// <summary>Contains dependency-free executable assertions for the simulator bootstrap contract.</summary>
public static class SimulatorBaselineTests
{
    private static readonly DateTimeOffset Epoch = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Executes the bootstrap proof groups for the central conformance command.</summary>
    /// <returns>Zero after all proof groups pass; assertion failures terminate with a nonzero process result.</returns>
    public static int Main()
    {
        RunAll();
        return 0;
    }

    /// <summary>Runs all bootstrap assertions and throws on the first contract violation.</summary>
    public static void RunAll()
    {
        RejectAndAcceptAreDeterministic();
        LostResponseProducesPossibleDispatch();
        DelayedObservationsUseVirtualTime();
        DisagreementIsRetainedWithoutAdjudication();
        WithinQuestionConflictsRemainScoped();
        RevisionChangesRejectStaleRequestsBeforeDispatch();
        ResultsOwnBoundedTraceStorage();
    }

    private static void RejectAndAcceptAreDeterministic()
    {
        Assert(Execute(BootstrapScenarios.RejectBeforeSend()).State == ExternalEffectState.ConfirmedNotOccurred, "reject must establish non-occurrence");
        var first = Execute(BootstrapScenarios.Accept());
        var second = Execute(BootstrapScenarios.Accept());
        Assert(first.State == ExternalEffectState.ConfirmedOccurred, "accept must confirm occurrence");
        Assert(Golden(first) == Golden(second), "identical scenario must produce identical trace");
    }

    private static void LostResponseProducesPossibleDispatch()
    {
        var result = Execute(BootstrapScenarios.PossibleDispatch());
        Assert(result.State == ExternalEffectState.PossibleDispatch, "lost response after send must remain PossibleDispatch");
        Assert(!result.State.Equals(ExternalEffectState.ConfirmedNotOccurred), "uncertainty cannot flatten to failure");
    }

    private static void DelayedObservationsUseVirtualTime()
    {
        var result = Execute(BootstrapScenarios.DelayedAgreement());
        Assert(result.Trace[^1].ObservedAtUtc == Epoch.AddSeconds(9), "delayed webhook must observe exact virtual time");
        Assert(result.State == ExternalEffectState.ConfirmedOccurred, "agreeing delayed observations must confirm occurrence");
    }

    private static void DisagreementIsRetainedWithoutAdjudication()
    {
        var result = Execute(BootstrapScenarios.SettlementDisagreement());
        Assert(!result.HasDisagreement, "cross-question mismatch must not fabricate within-question disagreement");
        Assert(result.State == ExternalEffectState.ConfirmedOccurred, "settlement non-inclusion must not overwrite occurrence evidence");
        Assert(result.SettlementState == SimulatorSettlementState.NotIncluded && result.HasCrossAuthorityMismatch,
            "settlement mismatch must remain independently visible");
        const string expected = "0@2030-01-01T00:00:00.0000000+00:00:CrossSendBoundary:Dispatching:send-boundary-crossed\n1@2030-01-01T00:00:02.0000000+00:00:Poll:ConfirmedOccurred:poll-observed\n2@2030-01-01T00:00:04.0000000+00:00:Webhook:ConfirmedOccurred:webhook-observed\n3@2030-01-02T00:00:00.0000000+00:00:Settlement:ConfirmedOccurred:settlement-observed";
        Assert(Golden(result) == expected, "ambiguity golden trace changed");
    }

    private static void WithinQuestionConflictsRemainScoped()
    {
        var settlement = Execute(BootstrapScenarios.SettlementConflict());
        Assert(settlement.State == ExternalEffectState.ConfirmedOccurred && settlement.HasDisagreement &&
            settlement.SettlementState == SimulatorSettlementState.Conflicted && !settlement.HasCrossAuthorityMismatch,
            "settlement conflict escaped its exact question");
        var occurrence = Execute(BootstrapScenarios.OccurrenceConflict());
        Assert(occurrence.State == ExternalEffectState.PossibleDispatch && occurrence.HasDisagreement &&
            occurrence.SettlementState == SimulatorSettlementState.Unknown && !occurrence.HasCrossAuthorityMismatch,
            "occurrence conflict escaped its exact question");
    }

    private static void RevisionChangesRejectStaleRequestsBeforeDispatch()
    {
        var engine = NewEngine();
        _ = engine.Execute(Request(), BootstrapScenarios.RevisionRotation(2, 2), new(Epoch));
        var stale = engine.Execute(Request(), BootstrapScenarios.Accept(), new(Epoch));
        Assert(stale.State == ExternalEffectState.NotDispatched && stale.Trace.Count == 1 && stale.Trace[0].Code == "revision-mismatch", "stale revisions must reject before send");
    }

    private static void ResultsOwnBoundedTraceStorage()
    {
        var input = new[] { new SimulatorEvent(TimeSpan.Zero, SimulatorEventKind.Reject) };
        var scenario = new SimulatorScenario("owned-input", input);
        input[0] = new(TimeSpan.Zero, SimulatorEventKind.Accept);
        var result = Execute(scenario);
        Assert(result.State == ExternalEffectState.ConfirmedNotOccurred, "scenario must own copied input");
        Assert(result.Trace.Count <= SimulatorEngine.MaximumTraceEntries, "result trace must remain bounded");
    }

    private static SimulatorResult Execute(SimulatorScenario scenario) => NewEngine().Execute(Request(), scenario, new(Epoch));
    private static SimulatorEngine NewEngine() => new(Revision.Create("credential", 1), Revision.Create("configuration", 1));
    private static SimulatorRequest Request() => new("operation-1", Revision.Create("credential", 1), Revision.Create("configuration", 1));
    private static string Golden(SimulatorResult result) => string.Join('\n', result.Trace.Select(static x => $"{x.Sequence}@{x.ObservedAtUtc:O}:{x.Event}:{x.State}:{x.Code}"));
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
