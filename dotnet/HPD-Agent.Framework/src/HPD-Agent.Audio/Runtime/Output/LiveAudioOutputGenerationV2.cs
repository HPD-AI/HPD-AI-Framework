using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Output;

internal abstract record LiveAudioOutputActivationResultV2
{
    private LiveAudioOutputActivationResultV2() { }
    internal sealed record Activated(AcceptedOutputOfferV2 Receipt, InMemoryOutputControllerV2 Controller) : LiveAudioOutputActivationResultV2;
    internal sealed record Duplicate(AcceptedOutputOfferV2 Receipt, InMemoryOutputControllerV2 Controller) : LiveAudioOutputActivationResultV2;
    internal sealed record Rejected(BoundedAscii SafeCode) : LiveAudioOutputActivationResultV2;
}

internal sealed class LiveAudioOutputGenerationV2
{
    private readonly object _gate = new();
    private readonly ushort _maximumOffers;
    private readonly ushort _maximumOutputReceipts;
    private OutputOfferAcceptanceStateV2 _offers = new();
    private readonly Dictionary<OperationId, InMemoryOutputControllerV2> _controllers = [];

    internal LiveAudioOutputGenerationV2(ExpectedAuthorityVectorV1 authority, ushort maximumOffers = 64,
        ushort maximumOutputReceipts = 256)
    {
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        if (maximumOffers == 0) throw new ArgumentOutOfRangeException(nameof(maximumOffers));
        if (maximumOutputReceipts == 0) throw new ArgumentOutOfRangeException(nameof(maximumOutputReceipts));
        var outputs = authority.Axes.Select(static axis => axis.Value).OfType<AuthorityAxisValueV1.Output>().ToArray();
        if (outputs.Length != 1) throw new ArgumentException("A prepared output generation requires exactly one Output axis.", nameof(authority));
        OutputGeneration = outputs[0].Value;
        _maximumOffers = maximumOffers;
        _maximumOutputReceipts = maximumOutputReceipts;
    }

    internal ExpectedAuthorityVectorV1 Authority { get; }
    internal OutputGenerationId OutputGeneration { get; }

    internal static LiveAudioOutputGenerationV2? TryCreate(ExpectedAuthorityVectorV1 authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return authority.Axes.Count(static axis => axis.Value is AuthorityAxisValueV1.Output) == 0
            ? null
            : new LiveAudioOutputGenerationV2(authority);
    }

    internal LiveAudioOutputActivationResultV2 Activate(OutputOfferV2 offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        lock (_gate)
        {
            if (!SameAuthority(Authority, offer.Origin.Decision.Authority) || offer.OutputGeneration != OutputGeneration)
                return new LiveAudioOutputActivationResultV2.Rejected(new BoundedAscii("output-generation-stale"));
            var result = OutputOfferCoordinatorV2.Accept(_offers, offer, _maximumOffers, _maximumOutputReceipts);
            if (result is OutputOfferResultV2.Rejected rejected)
                return new LiveAudioOutputActivationResultV2.Rejected(rejected.SafeCode);
            if (result is OutputOfferResultV2.Duplicate duplicate)
                return new LiveAudioOutputActivationResultV2.Duplicate(duplicate.Receipt, _controllers[offer.OperationId]);
            var accepted = (OutputOfferResultV2.Accepted)result;
            _offers = accepted.State;
            _controllers.Add(offer.OperationId, accepted.Controller);
            return new LiveAudioOutputActivationResultV2.Activated(accepted.Receipt, accepted.Controller);
        }
    }

    private static bool SameAuthority(ExpectedAuthorityVectorV1 left, ExpectedAuthorityVectorV1 right) =>
        left.Session == right.Session && left.Axes.SequenceEqual(right.Axes);
}
