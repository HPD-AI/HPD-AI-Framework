using HPD.Payments.Connectors.Simulator.Core;

namespace HPD.Payments.Connectors.Simulator.Scenarios;

/// <summary>Provides stable deterministic scenarios for bootstrap integration and ambiguity testing.</summary>
public static class BootstrapScenarios
{
    /// <summary>Creates a definite pre-send rejection scenario.</summary>
    public static SimulatorScenario RejectBeforeSend() => new("reject-before-send",
        [new(TimeSpan.Zero, SimulatorEventKind.Reject)]);

    /// <summary>Creates a synchronous accepted-and-confirmed occurrence scenario.</summary>
    public static SimulatorScenario Accept() => new("accept",
        [new(TimeSpan.Zero, SimulatorEventKind.CrossSendBoundary), new(TimeSpan.FromMilliseconds(1), SimulatorEventKind.Poll, SimulatorOccurrence.Occurred)]);

    /// <summary>Creates a lost-response scenario that must conservatively produce PossibleDispatch.</summary>
    public static SimulatorScenario PossibleDispatch() => new("possible-dispatch",
        [new(TimeSpan.Zero, SimulatorEventKind.CrossSendBoundary), new(TimeSpan.FromMilliseconds(1), SimulatorEventKind.LoseResponse)]);

    /// <summary>Creates delayed poll and webhook observations that agree occurrence was confirmed.</summary>
    public static SimulatorScenario DelayedAgreement() => new("delayed-agreement",
        [new(TimeSpan.Zero, SimulatorEventKind.CrossSendBoundary), new(TimeSpan.FromSeconds(5), SimulatorEventKind.Poll, SimulatorOccurrence.Occurred), new(TimeSpan.FromSeconds(9), SimulatorEventKind.Webhook, SimulatorOccurrence.Occurred)]);

    /// <summary>Creates confirmed occurrence plus settlement non-inclusion as a retained cross-authority mismatch.</summary>
    public static SimulatorScenario SettlementDisagreement() => new("settlement-disagreement",
        [new(TimeSpan.Zero, SimulatorEventKind.CrossSendBoundary), new(TimeSpan.FromSeconds(2), SimulatorEventKind.Poll, SimulatorOccurrence.Occurred), new(TimeSpan.FromSeconds(4), SimulatorEventKind.Webhook, SimulatorOccurrence.Occurred), new(TimeSpan.FromDays(1), SimulatorEventKind.Settlement, SimulatorOccurrence.NotOccurred)]);

    /// <summary>Creates conflicting claims within settlement authority while leaving occurrence unchanged.</summary>
    public static SimulatorScenario SettlementConflict() => new("settlement-conflict",
        [new(TimeSpan.Zero, SimulatorEventKind.CrossSendBoundary), new(TimeSpan.FromSeconds(1), SimulatorEventKind.Poll, SimulatorOccurrence.Occurred), new(TimeSpan.FromHours(1), SimulatorEventKind.Settlement, SimulatorOccurrence.Occurred), new(TimeSpan.FromHours(2), SimulatorEventKind.Settlement, SimulatorOccurrence.NotOccurred)]);

    /// <summary>Creates conflicting poll observations within occurrence authority.</summary>
    public static SimulatorScenario OccurrenceConflict() => new("occurrence-conflict",
        [new(TimeSpan.Zero, SimulatorEventKind.CrossSendBoundary), new(TimeSpan.FromSeconds(1), SimulatorEventKind.Poll, SimulatorOccurrence.Occurred), new(TimeSpan.FromSeconds(2), SimulatorEventKind.Poll, SimulatorOccurrence.NotOccurred)]);

    /// <summary>Creates explicit credential and configuration revision rotation events.</summary>
    public static SimulatorScenario RevisionRotation(ulong credential, ulong configuration) => new("revision-rotation",
        [new(TimeSpan.Zero, SimulatorEventKind.RotateCredential, revisionValue: credential), new(TimeSpan.FromTicks(1), SimulatorEventKind.RotateConfiguration, revisionValue: configuration)]);
}
