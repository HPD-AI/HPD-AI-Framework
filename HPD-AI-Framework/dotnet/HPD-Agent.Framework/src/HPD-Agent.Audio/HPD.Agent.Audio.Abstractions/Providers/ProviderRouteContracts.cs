using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Policies;

namespace HPD.Agent.Audio.Providers;

public interface IProviderRoute : IAsyncDisposable
{
    ProviderRouteId Id { get; }

    ProviderRouteState State { get; }

    ProviderRouteEpoch CurrentEpoch { get; }

    IAsyncEnumerable<ProviderRouteDecision> ReadDecisionsAsync(CancellationToken cancellationToken = default);

    ValueTask<ProviderRouteDecision> SelectAsync(
        ProviderRouteRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderRouteRequest
{
    public required AudioSessionId SessionId { get; init; }

    public required IReadOnlyList<CanonicalMediaEnvelope> Inputs { get; init; }

    public bool HasTextInput { get; init; }

    public required AudioPolicySet PolicySet { get; init; }

    public IReadOnlyList<ProviderCapabilityProfile> Candidates { get; init; } = [];
}

public sealed record ProviderRouteDecision
{
    public required ProviderRouteId RouteId { get; init; }

    public required ProviderRouteDecisionKind Kind { get; init; }

    public required ProviderRouteEpoch Epoch { get; init; }

    public InteractionExecutionPlan? Plan { get; init; }

    public required string Reason { get; init; }
}

public enum ProviderRouteDecisionKind
{
    OpenCandidate = 0,
    UseCurrent = 1,
    ReferenceOnly = 2,
    Reject = 3,
    Fail = 4,
    Degrade = 5,
    Switch = 6,
    Retry = 7
}

public sealed record ProviderRouteEpoch
{
    public required ProviderRouteEpochId Id { get; init; }

    public required string ProviderKey { get; init; }

    public required DateTimeOffset StartedAt { get; init; }
}
