namespace HPD.Events;

/// <summary>
/// An opaque, generation-bound capability proving that a replay timeline emitted
/// one complete frame from contracted sources.
/// </summary>
/// <typeparam name="TEvent">The replayed event type.</typeparam>
public readonly struct FinalizedReplayFrameHandle<TEvent> where TEvent : Event
{
    private readonly FinalizedReplayFrameOwner<TEvent>? _owner;

    internal FinalizedReplayFrameHandle(
        FinalizedReplayFrameOwner<TEvent> owner,
        long ownerId,
        int generation,
        long frameSlot,
        string contentDigest)
    {
        _owner = owner;
        OwnerId = ownerId;
        Generation = generation;
        FrameSlot = frameSlot;
        ContentDigest = contentDigest;
    }

    /// <summary>Gets the issuing timeline owner identity.</summary>
    public long OwnerId { get; }
    /// <summary>Gets the issuing read generation.</summary>
    public int Generation { get; }
    /// <summary>Gets the finalized frame slot in that generation.</summary>
    public long FrameSlot { get; }
    /// <summary>Gets the immutable frame content and provenance digest.</summary>
    public string? ContentDigest { get; }
    /// <summary>Gets whether the handle is structurally specified; liveness still requires validation.</summary>
    public bool IsSpecified => _owner is not null && OwnerId > 0 && Generation > 0 && FrameSlot >= 0 &&
        !string.IsNullOrWhiteSpace(ContentDigest);

    /// <summary>Validates current owner/generation/slot/digest and borrows the finalized frame.</summary>
    public bool TryGetFrame(out ReplayFrame<TEvent>? frame)
    {
        if (_owner is not null)
            return _owner.TryGet(OwnerId, Generation, FrameSlot, ContentDigest, out frame);
        frame = null;
        return false;
    }
}

internal sealed class FinalizedReplayFrameOwner<TEvent> where TEvent : Event
{
    private readonly long _ownerId;
    private ReplayFrame<TEvent>? _frame;
    private int _generation;
    private long _slot = -1;
    private string? _digest;
    private string[]? _eventDigests;
    private int _live;

    internal FinalizedReplayFrameOwner(long ownerId)
    {
        _ownerId = ownerId;
    }

    internal void Publish(ReplayFrame<TEvent> frame, int generation, long slot, string digest)
    {
        _frame = frame;
        _generation = generation;
        _slot = slot;
        _digest = digest;
        _eventDigests = new string[frame.Entries.Count];
        for (int i = 0; i < frame.Entries.Count; i++)
        {
            if (frame.Entries[i].Event is not IReplayContentDigest content ||
                string.IsNullOrWhiteSpace(content.ReplayContentDigest))
                throw new InvalidOperationException("A finalized frame event requires a canonical content digest.");
            _eventDigests[i] = content.ReplayContentDigest;
        }
        Volatile.Write(ref _live, 1);
    }

    internal bool TryGet(long ownerId, int generation, long slot, string? digest, out ReplayFrame<TEvent>? frame)
    {
        ReplayFrame<TEvent>? candidate = _frame;
        bool valid = Volatile.Read(ref _live) != 0 && candidate is not null && ownerId == _ownerId &&
            generation == _generation && slot == _slot && StringComparer.Ordinal.Equals(digest, _digest) &&
            IsContentUnchanged(candidate);
        frame = valid ? candidate : null;
        return valid;
    }

    internal void Release()
    {
        Volatile.Write(ref _live, 0);
        _frame = null;
        _digest = null;
        _eventDigests = null;
        _slot = -1;
    }

    private bool IsContentUnchanged(ReplayFrame<TEvent> candidate)
    {
        string[]? issuedDigests = _eventDigests;
        if (issuedDigests is null || issuedDigests.Length != candidate.Entries.Count)
            return false;
        for (int i = 0; i < issuedDigests.Length; i++)
        {
            if (candidate.Entries[i].Event is not IReplayContentDigest content ||
                !StringComparer.Ordinal.Equals(content.ReplayContentDigest, issuedDigests[i]))
                return false;
        }
        return true;
    }
}
