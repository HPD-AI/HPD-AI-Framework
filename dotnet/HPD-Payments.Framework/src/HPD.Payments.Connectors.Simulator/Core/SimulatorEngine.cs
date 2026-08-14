using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Connectors.Simulator.Core;

/// <summary>Executes explicit provider scenarios without network, wall-clock, discovery, or ambient configuration.</summary>
/// <remarks>The engine reports observations; it never adjudicates disagreement or grants runtime mutation authority.</remarks>
public sealed class SimulatorEngine
{
    /// <summary>Specifies the maximum trace entries retained by one execution.</summary>
    public const int MaximumTraceEntries = SimulatorScenario.MaximumEvents + 1;
    private Revision _credentialRevision;
    private Revision _configurationRevision;

    /// <summary>Creates a simulator pinned to explicit active credential and configuration revisions.</summary>
    /// <exception cref="ArgumentException">Either revision is invalid or has the wrong semantic kind.</exception>
    public SimulatorEngine(Revision credentialRevision, Revision configurationRevision)
    {
        if (!credentialRevision.IsValid || credentialRevision.Kind != "credential" ||
            !configurationRevision.IsValid || configurationRevision.Kind != "configuration")
            throw new ArgumentException("Simulator revisions must use credential and configuration kinds.");
        _credentialRevision = credentialRevision; _configurationRevision = configurationRevision;
    }

    /// <summary>Executes a scenario against an explicit virtual clock and returns an owned bounded trace.</summary>
    /// <exception cref="ArgumentNullException">The scenario or virtual clock is null.</exception>
    public SimulatorResult Execute(SimulatorRequest request, SimulatorScenario scenario, SimulatorVirtualTime time)
    {
        ArgumentNullException.ThrowIfNull(scenario); ArgumentNullException.ThrowIfNull(time);
        var trace = new List<SimulatorTraceEntry>(MaximumTraceEntries);
        var state = ExternalEffectState.NotDispatched;
        var sawOccurred = false;
        var sawNotOccurred = false;

        if (request.CredentialRevision != _credentialRevision || request.ConfigurationRevision != _configurationRevision)
        {
            trace.Add(new(0, time.UtcNow, SimulatorEventKind.Reject, state, "revision-mismatch"));
            return new(state, false, trace.ToArray());
        }

        foreach (var item in scenario.Events)
        {
            time.AdvanceTo(item.Offset);
            var code = item.Kind switch
            {
                SimulatorEventKind.Reject => "rejected-before-send",
                SimulatorEventKind.Accept => "accepted",
                SimulatorEventKind.CrossSendBoundary => "send-boundary-crossed",
                SimulatorEventKind.LoseResponse => "response-lost-after-send",
                SimulatorEventKind.Poll => "poll-observed",
                SimulatorEventKind.Webhook => "webhook-observed",
                SimulatorEventKind.Settlement => "settlement-observed",
                SimulatorEventKind.RotateCredential => "credential-revision-rotated",
                SimulatorEventKind.RotateConfiguration => "configuration-revision-rotated",
                _ => throw new InvalidOperationException("Unreachable simulator event."),
            };

            switch (item.Kind)
            {
                case SimulatorEventKind.Reject when state == ExternalEffectState.NotDispatched:
                    state = ExternalEffectState.ConfirmedNotOccurred;
                    break;
                case SimulatorEventKind.Accept:
                case SimulatorEventKind.CrossSendBoundary:
                    state = ExternalEffectState.Dispatching;
                    break;
                case SimulatorEventKind.LoseResponse when state != ExternalEffectState.NotDispatched:
                    state = ExternalEffectState.PossibleDispatch;
                    break;
                case SimulatorEventKind.Poll:
                case SimulatorEventKind.Webhook:
                case SimulatorEventKind.Settlement:
                    sawOccurred |= item.Occurrence == SimulatorOccurrence.Occurred;
                    sawNotOccurred |= item.Occurrence == SimulatorOccurrence.NotOccurred;
                    state = sawOccurred && sawNotOccurred ? ExternalEffectState.PossibleDispatch :
                        sawOccurred ? ExternalEffectState.ConfirmedOccurred : ExternalEffectState.ConfirmedNotOccurred;
                    break;
                case SimulatorEventKind.RotateCredential:
                    _credentialRevision = Revision.Create("credential", item.RevisionValue);
                    break;
                case SimulatorEventKind.RotateConfiguration:
                    _configurationRevision = Revision.Create("configuration", item.RevisionValue);
                    break;
            }
            trace.Add(new(trace.Count, time.UtcNow, item.Kind, state, code));
        }
        return new(state, sawOccurred && sawNotOccurred, trace.ToArray());
    }
}
