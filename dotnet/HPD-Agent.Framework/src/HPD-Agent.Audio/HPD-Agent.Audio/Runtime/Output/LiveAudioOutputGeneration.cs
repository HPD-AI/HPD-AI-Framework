using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Output;

internal abstract record LiveAudioOutputActivationResult
{
    private LiveAudioOutputActivationResult() { }
    internal sealed record Activated(AcceptedOutputOffer Receipt, InMemoryOutputController Controller) : LiveAudioOutputActivationResult;
    internal sealed record Duplicate(AcceptedOutputOffer Receipt, InMemoryOutputController Controller) : LiveAudioOutputActivationResult;
    internal sealed record Rejected(BoundedAscii SafeCode) : LiveAudioOutputActivationResult;
}

internal sealed class LiveAudioOutputGeneration
{
    private readonly object _gate = new();
    private readonly ushort _maximumOffers;
    private readonly ushort _maximumOutputReceipts;
    private OutputOfferAcceptanceState _offers = new();
    private readonly Dictionary<OperationId, InMemoryOutputController> _controllers = [];

    internal LiveAudioOutputGeneration(ExpectedAuthorityVectorV1 authority, ushort maximumOffers = 64,
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

    internal static LiveAudioOutputGeneration? TryCreate(ExpectedAuthorityVectorV1 authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return authority.Axes.Count(static axis => axis.Value is AuthorityAxisValueV1.Output) == 0
            ? null
            : new LiveAudioOutputGeneration(authority);
    }

    internal LiveAudioOutputActivationResult Activate(OutputOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        lock (_gate)
        {
            if (!SameAuthority(Authority, offer.Origin.Decision.Authority) || offer.OutputGeneration != OutputGeneration)
                return new LiveAudioOutputActivationResult.Rejected(new BoundedAscii("output-generation-stale"));
            var result = OutputOfferCoordinator.Accept(_offers, offer, _maximumOffers, _maximumOutputReceipts);
            if (result is OutputOfferResult.Rejected rejected)
                return new LiveAudioOutputActivationResult.Rejected(rejected.SafeCode);
            if (result is OutputOfferResult.Duplicate duplicate)
                return new LiveAudioOutputActivationResult.Duplicate(duplicate.Receipt, _controllers[offer.OperationId]);
            var accepted = (OutputOfferResult.Accepted)result;
            _offers = accepted.State;
            _controllers.Add(offer.OperationId, accepted.Controller);
            return new LiveAudioOutputActivationResult.Activated(accepted.Receipt, accepted.Controller);
        }
    }

    private static bool SameAuthority(ExpectedAuthorityVectorV1 left, ExpectedAuthorityVectorV1 right) =>
        left.Session == right.Session && left.Axes.SequenceEqual(right.Axes);
}
