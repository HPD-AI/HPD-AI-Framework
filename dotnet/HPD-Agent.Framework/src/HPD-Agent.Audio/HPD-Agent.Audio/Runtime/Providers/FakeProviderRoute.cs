using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.Providers;

namespace HPD.Agent.Audio.Runtime.Providers;

public sealed class FakeProviderRoute : IProviderRoute
{
    private readonly List<ProviderRouteDecision> _decisions = [];
    private readonly RuntimeClock _clock;
    private readonly RuntimeIdFactory _ids;
    private readonly ProviderCapabilityProfile _defaultProfile;

    public FakeProviderRoute(
        RuntimeIdFactory? ids = null,
        RuntimeClock? clock = null,
        string providerKey = "fake-stt",
        ProviderCapabilityFlag capabilities = ProviderCapabilityFlag.SpeechToText)
    {
        _ids = ids ?? new RuntimeIdFactory();
        _clock = clock ?? new RuntimeClock();
        _defaultProfile = new ProviderCapabilityProfile
        {
            ProviderKey = providerKey,
            Declared = new ProviderDeclaredCapabilities
            {
                Flags = capabilities
            }
        };
        Id = _ids.NextProviderRouteId();
        CurrentEpoch = new ProviderRouteEpoch
        {
            Id = _ids.NextProviderRouteEpochId(),
            ProviderKey = providerKey,
            StartedAt = _clock.UtcNow
        };
    }

    public ProviderRouteId Id { get; }

    public ProviderRouteState State { get; private set; } = ProviderRouteState.Ready;

    public ProviderRouteEpoch CurrentEpoch { get; private set; }

    public async IAsyncEnumerable<ProviderRouteDecision> ReadDecisionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var decision in _decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return decision;
            await Task.Yield();
        }
    }

    public ValueTask<ProviderRouteDecision> SelectAsync(
        ProviderRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = ProviderRouteState.Active;
        IReadOnlyList<ProviderCapabilityProfile> profiles = request.Candidates.Count > 0 ? request.Candidates : [_defaultProfile];
        var selected = Select(request.PolicySet.InputMedia, request.Inputs, request.HasTextInput, profiles);
        CurrentEpoch = CreateEpoch(selected.Profile?.ProviderKey ?? _defaultProfile.ProviderKey);

        var decision = new ProviderRouteDecision
        {
            RouteId = Id,
            Kind = selected.Kind,
            Epoch = CurrentEpoch,
            Plan = selected.Profile is null || selected.Topology is null
                ? null
                : CreatePlan(selected.Profile, selected.Topology.Value),
            Reason = selected.Reason
        };

        _decisions.Add(decision);
        return ValueTask.FromResult(decision);
    }

    public ValueTask DisposeAsync()
    {
        State = ProviderRouteState.Stopped;
        return ValueTask.CompletedTask;
    }

    private ProviderRouteEpoch CreateEpoch(string providerKey)
        => new()
        {
            Id = _ids.NextProviderRouteEpochId(),
            ProviderKey = providerKey,
            StartedAt = _clock.Tick()
        };

    private InteractionExecutionPlan CreatePlan(
        ProviderCapabilityProfile profile,
        AudioInteractionTopology topology)
        => new()
        {
            Topology = topology,
            RouteEpoch = CurrentEpoch,
            Capabilities = profile,
            ResponseOwnership = ProviderResponseOwnership.HpdChatOwnsResponse,
            Metadata = new AudioExtensionData(new Dictionary<string, object?>
            {
                ["routeKind"] = "fake",
                ["routeId"] = Id.Value
            })
        };

    private static FakeRouteSelection Select(
        InputMediaPolicy policy,
        IReadOnlyList<CanonicalMediaEnvelope> inputs,
        bool hasTextInput,
        IReadOnlyList<ProviderCapabilityProfile> profiles)
    {
        if (inputs.Count == 0)
        {
            return new FakeRouteSelection(
                ProviderRouteDecisionKind.ReferenceOnly,
                null,
                null,
                "input-media-reference-only-no-media");
        }

        if (policy.HandlingMode is InputMediaHandlingMode.Reject)
        {
            return new FakeRouteSelection(
                ProviderRouteDecisionKind.Reject,
                null,
                null,
                "input-media-policy-rejected");
        }

        if (policy.HandlingMode is InputMediaHandlingMode.ReferenceOnly)
        {
            return new FakeRouteSelection(
                ProviderRouteDecisionKind.ReferenceOnly,
                null,
                null,
                "input-media-reference-only");
        }

        var speechToText = profiles.FirstOrDefault(profile => Has(profile, ProviderCapabilityFlag.SpeechToText));

        if (speechToText is not null && policy.AllowBatchTranscription)
        {
            return new FakeRouteSelection(
                ProviderRouteDecisionKind.OpenCandidate,
                speechToText,
                AudioInteractionTopology.SplitSpeechToTextChatTextToSpeech,
                "fake-speech-to-text-selected");
        }

        return new FakeRouteSelection(
            ProviderRouteDecisionKind.Fail,
            null,
            null,
            "input-media-no-usable-provider");
    }

    private static bool Has(ProviderCapabilityProfile profile, ProviderCapabilityFlag flag)
        => (profile.Declared.Flags & flag) == flag;

    private sealed record FakeRouteSelection(
        ProviderRouteDecisionKind Kind,
        ProviderCapabilityProfile? Profile,
        AudioInteractionTopology? Topology,
        string Reason);
}
