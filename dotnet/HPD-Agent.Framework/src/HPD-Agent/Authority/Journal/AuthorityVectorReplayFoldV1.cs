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
        var axes = new Dictionary<AuthorityAxisId, StableId128>();
        var expectedPosition = 1L;
        RuntimeGenerationId? replacement = null;
        foreach (var fact in facts)
        {
            if (fact is null || fact.Position.Session != session || fact.Position.Sequence != expectedPosition || replacement is not null)
                return Invalid(expectedPosition - 1);

            var initializationResult = AuthorityGenerationInitializationCodecV1.Decode(
                fact.PayloadSchema, fact.Owner, session, fact.PayloadMemory, out var initialization);
            if (initializationResult == AuthorityGenerationInitializationDecodeV1.Invalid)
                return Invalid(expectedPosition - 1);
            if (initializationResult == AuthorityGenerationInitializationDecodeV1.Valid)
            {
                if (!axes.TryAdd(initialization.Axis, initialization.Initial))
                    return Invalid(expectedPosition - 1);
                expectedPosition++;
                continue;
            }

            var transitionResult = AuthorityGenerationTransitionCodecV1.Decode(
                fact.PayloadSchema, fact.Owner, session, fact.PayloadMemory, out var transition);
            if (transitionResult == AuthorityGenerationTransitionDecodeV1.Invalid)
                return Invalid(expectedPosition - 1);
            if (transitionResult == AuthorityGenerationTransitionDecodeV1.Valid)
            {
                if (transition.Axis == AuthorityAxisId.Runtime)
                {
                    if (RuntimeGenerationId.FromValue(transition.ExpectedPrevious) != session.RuntimeGenerationId)
                        return Invalid(expectedPosition - 1);
                    replacement = RuntimeGenerationId.FromValue(transition.ProposedNext);
                }
                else if (!axes.TryGetValue(transition.Axis, out var current) || !current.Equals(transition.ExpectedPrevious))
                {
                    return Invalid(expectedPosition - 1);
                }
                else
                {
                    axes[transition.Axis] = transition.ProposedNext;
                }
            }
            expectedPosition++;
        }

        var last = expectedPosition - 1;
        if (replacement is { } next)
            return new AuthorityVectorReplayResultV1.GenerationReplaced(next, last);
        var values = axes.OrderBy(static pair => pair.Key)
            .Select(static pair => ToValue(pair.Key, pair.Value));
        return new AuthorityVectorReplayResultV1.Current(new CurrentAuthorityVectorSnapshotV1(session, values, last));
    }

    private static AuthorityVectorReplayResultV1 Invalid(long last) =>
        new AuthorityVectorReplayResultV1.InvalidHistory(last);

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
}
