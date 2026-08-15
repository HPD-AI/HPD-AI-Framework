using System.Collections.Immutable;

namespace HPD.Agent.Authority;

internal sealed class CurrentAuthorityVectorSnapshotV1
{
    internal CurrentAuthorityVectorSnapshotV1(
        SessionAuthorityStampV1 session,
        IEnumerable<AuthorityAxisValueV1> values,
        long throughPosition)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        if (throughPosition < 0) throw new ArgumentOutOfRangeException(nameof(throughPosition));
        ArgumentNullException.ThrowIfNull(values);
        Session = session;
        Axes = values.Select(static value => new AxisEntryV1(value))
            .OrderBy(static entry => entry.AxisId).ToImmutableArray();
        if (Axes.Zip(Axes.Skip(1), static (left, right) => left.AxisId == right.AxisId).Any(static duplicate => duplicate))
            throw new ArgumentException("Current axes must be unique.", nameof(values));
        ThroughPosition = throughPosition;
    }

    internal SessionAuthorityStampV1 Session { get; }
    internal ImmutableArray<AxisEntryV1> Axes { get; }
    internal long ThroughPosition { get; }
}

internal abstract record AuthorityVectorReplayResultV1
{
    private AuthorityVectorReplayResultV1() { }

    internal sealed record Current : AuthorityVectorReplayResultV1
    {
        internal Current(CurrentAuthorityVectorSnapshotV1 snapshot) =>
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        internal CurrentAuthorityVectorSnapshotV1 Snapshot { get; }
    }

    internal sealed record GenerationReplaced : AuthorityVectorReplayResultV1
    {
        internal GenerationReplaced(RuntimeGenerationId replacedBy, long lastPosition)
        {
            if (!replacedBy.IsValid) throw new ArgumentException("A replacement runtime generation is required.", nameof(replacedBy));
            if (lastPosition < 0) throw new ArgumentOutOfRangeException(nameof(lastPosition));
            ReplacedBy = replacedBy;
            LastPosition = lastPosition;
        }

        internal RuntimeGenerationId ReplacedBy { get; }
        internal long LastPosition { get; }
    }

    internal sealed record InvalidHistory : AuthorityVectorReplayResultV1
    {
        internal InvalidHistory(long lastPosition)
        {
            if (lastPosition < 0) throw new ArgumentOutOfRangeException(nameof(lastPosition));
            LastPosition = lastPosition;
        }

        internal long LastPosition { get; }
    }
}

internal static class AuthorityVectorReplayFoldV1
{
    internal static AuthorityVectorReplayResultV1 Fold(
        SessionAuthorityStampV1 session,
        IEnumerable<AuthorityFactEnvelopeV1> facts)
    {
        if (!session.IsValid) throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        ArgumentNullException.ThrowIfNull(facts);
        var accumulator = new AuthorityVectorReplayAccumulatorV1(session);
        foreach (var fact in facts)
            accumulator.Apply(fact);
        return accumulator.Complete();
    }

    internal static AuthorityVectorReplayAccumulatorV1 CreateAccumulator(SessionAuthorityStampV1 session) => new(session);

    private static AuthorityAxisValueV1 ToValue(AuthorityAxisId axis, StableId128 value) => axis switch
    {
        AuthorityAxisId.Graph => new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(value)),
        AuthorityAxisId.Activity => new AuthorityAxisValueV1.Activity(ActivityGenerationId.FromValue(value)),
        AuthorityAxisId.Turn => new AuthorityAxisValueV1.Turn(TurnGenerationId.FromValue(value)),
        AuthorityAxisId.Provider => new AuthorityAxisValueV1.Provider(ProviderGenerationId.FromValue(value)),
        AuthorityAxisId.Output => new AuthorityAxisValueV1.Output(OutputGenerationId.FromValue(value)),
        AuthorityAxisId.Sink => new AuthorityAxisValueV1.Sink(SinkGenerationId.FromValue(value)),
        AuthorityAxisId.Tool => new AuthorityAxisValueV1.Tool(ToolGenerationId.FromValue(value)),
        AuthorityAxisId.Route => new AuthorityAxisValueV1.Route(RouteGenerationId.FromValue(value)),
        AuthorityAxisId.Privacy => new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.FromValue(value)),
        AuthorityAxisId.Transport => new AuthorityAxisValueV1.Transport(TransportGenerationId.FromValue(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    internal sealed class AuthorityVectorReplayAccumulatorV1
    {
        private readonly SessionAuthorityStampV1 _session;
        private readonly Dictionary<AuthorityAxisId, StableId128> _axes = [];
        private long _expectedPosition = 1;
        private RuntimeGenerationId? _replacement;
        private bool _invalid;

        internal AuthorityVectorReplayAccumulatorV1(SessionAuthorityStampV1 session)
        {
            if (!session.IsValid) throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
            _session = session;
        }

        internal void Apply(AuthorityFactEnvelopeV1? fact)
        {
            if (_invalid) return;
            if (fact is null || fact.Position.Session != _session || fact.Position.Sequence != _expectedPosition || _replacement is not null)
            {
                _invalid = true;
                return;
            }
            var initializationResult = AuthorityGenerationInitializationCodecV1.Decode(
                fact.PayloadSchema, fact.Owner, _session, fact.PayloadMemory, out var initialization);
            if (initializationResult == AuthorityGenerationInitializationDecodeV1.Invalid)
            {
                _invalid = true;
                return;
            }
            if (initializationResult == AuthorityGenerationInitializationDecodeV1.Valid)
            {
                if (!_axes.TryAdd(initialization.Axis, initialization.Initial))
                {
                    _invalid = true;
                    return;
                }
                _expectedPosition++;
                return;
            }
            var transitionResult = AuthorityGenerationTransitionCodecV1.Decode(
                fact.PayloadSchema, fact.Owner, _session, fact.PayloadMemory, out var transition);
            if (transitionResult == AuthorityGenerationTransitionDecodeV1.Invalid)
            {
                _invalid = true;
                return;
            }
            if (transitionResult == AuthorityGenerationTransitionDecodeV1.Valid)
            {
                if (transition.Axis == AuthorityAxisId.Runtime)
                {
                    if (RuntimeGenerationId.FromValue(transition.ExpectedPrevious) != _session.RuntimeGenerationId)
                    {
                        _invalid = true;
                        return;
                    }
                    else
                        _replacement = RuntimeGenerationId.FromValue(transition.ProposedNext);
                }
                else if (!_axes.TryGetValue(transition.Axis, out var current) || !current.Equals(transition.ExpectedPrevious))
                {
                    _invalid = true;
                    return;
                }
                else
                {
                    _axes[transition.Axis] = transition.ProposedNext;
                }
            }
            _expectedPosition++;
        }

        internal AuthorityVectorReplayResultV1 Complete()
        {
            var last = _expectedPosition - 1;
            if (_invalid) return new AuthorityVectorReplayResultV1.InvalidHistory(last);
            if (_replacement is { } next) return new AuthorityVectorReplayResultV1.GenerationReplaced(next, last);
            var values = _axes.OrderBy(static pair => pair.Key).Select(static pair => ToValue(pair.Key, pair.Value));
            return new AuthorityVectorReplayResultV1.Current(new CurrentAuthorityVectorSnapshotV1(_session, values, last));
        }

        internal long LastVerifiedPosition => _expectedPosition - 1;
    }
}
