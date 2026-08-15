using System.Collections.Immutable;
using HPD.Agent.Authority;

namespace HPD.Agent.Middleware;

/// <summary>Describes one deterministically ordered neutral control participant.</summary>
public sealed record AgentControlParticipant
{
    /// <summary>Initializes one participant descriptor.</summary>
    /// <param name="key">The unique bounded participant key.</param>
    /// <param name="order">The nonnegative deterministic execution order.</param>
    /// <param name="hook">The participant hook.</param>
    /// <exception cref="ArgumentException">The key is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The order is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="hook"/> is null.</exception>
    public AgentControlParticipant(BoundedAscii key, int order, IAgentControlHook hook)
    {
        if (!key.IsValid) throw new ArgumentException("A participant key is required.", nameof(key));
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        Key = key;
        Order = order;
        Hook = hook ?? throw new ArgumentNullException(nameof(hook));
    }

    /// <summary>Gets the unique participant key.</summary>
    public BoundedAscii Key { get; }
    /// <summary>Gets the deterministic participant order.</summary>
    public int Order { get; }
    /// <summary>Gets the participant hook.</summary>
    public IAgentControlHook Hook { get; }
}

/// <summary>Runs an immutable bounded set of control hooks in deterministic order.</summary>
public sealed class CompositeAgentControlHook : IAgentControlHook
{
    /// <summary>Defines the maximum participant count in one composite.</summary>
    public const int MaximumParticipants = 32;
    private readonly ImmutableArray<AgentControlParticipant> _participants;

    /// <summary>Initializes an immutable composite.</summary>
    /// <param name="participants">Zero to 32 uniquely keyed participants.</param>
    /// <exception cref="ArgumentNullException"><paramref name="participants"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">More than 32 participants are supplied.</exception>
    /// <exception cref="ArgumentException">A participant is null or a key/order tuple is duplicated.</exception>
    public CompositeAgentControlHook(IEnumerable<AgentControlParticipant> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        var bounded = new List<AgentControlParticipant>(MaximumParticipants);
        foreach (var participant in participants)
        {
            if (participant is null) throw new ArgumentException("Participants cannot contain null.", nameof(participants));
            if (bounded.Count == MaximumParticipants) throw new ArgumentOutOfRangeException(nameof(participants));
            bounded.Add(participant);
        }
        if (bounded.Select(static participant => participant.Key).Distinct().Count() != bounded.Count ||
            bounded.Select(static participant => (participant.Order, participant.Key)).Distinct().Count() != bounded.Count)
            throw new ArgumentException("Participant keys and ordered key tuples must be unique.", nameof(participants));
        _participants = bounded.OrderBy(static participant => participant.Order)
            .ThenBy(static participant => participant.Key)
            .ToImmutableArray();
    }

    /// <inheritdoc />
    public async ValueTask<AgentControlObservationResult> ObserveAsync(
        AgentControlEnvelope envelope,
        CancellationToken waitCancellation = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        waitCancellation.ThrowIfCancellationRequested();
        var observed = false;
        foreach (var participant in _participants)
        {
            waitCancellation.ThrowIfCancellationRequested();
            AgentControlObservationResult result;
            try
            {
                result = await participant.Hook.ObserveAsync(envelope, waitCancellation).ConfigureAwait(false)
                    ?? new AgentControlObservationResult.Rejected(new BoundedAscii("null-hook-result"));
            }
            catch (OperationCanceledException) when (waitCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return new AgentControlObservationResult.Rejected(new BoundedAscii("hook-failed"));
            }
            if (result is AgentControlObservationResult.Rejected) return result;
            observed |= result is AgentControlObservationResult.Observed;
        }
        if (observed) return new AgentControlObservationResult.Observed();
        return new AgentControlObservationResult.NotHandled();
    }
}
