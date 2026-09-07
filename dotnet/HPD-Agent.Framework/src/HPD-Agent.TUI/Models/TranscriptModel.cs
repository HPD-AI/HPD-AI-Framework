namespace HPD.Agent.TUI.Models;

/// <summary>Controls a transcript mutation that may target terminal-visible committed history.</summary>
public enum CommittedHistoryMutationPolicy
{
    /// <summary>Leave both the model and terminal history unchanged.</summary>
    Reject,
    /// <summary>Apply the model mutation and require a visible presentation-epoch boundary.</summary>
    VisibleEpochBoundary,
    /// <summary>Apply the model mutation and require terminal scrollback clearing plus durable replay.</summary>
    ClearAndReplay,
    /// <summary>Apply the model mutation and require transition to the alternate screen.</summary>
    SwitchToAlternateScreen
}

/// <summary>Identifies whether a transcript mutation was applied and requires terminal recovery.</summary>
public enum TranscriptMutationStatus
{
    /// <summary>The mutation affected only retractable model state.</summary>
    Applied,
    /// <summary>The mutation was rejected because terminal-visible history cannot be retracted.</summary>
    CannotRetract,
    /// <summary>The mutation was applied and the selected terminal recovery policy must run.</summary>
    RequiresPresentationReset
}

/// <summary>Reports a transcript mutation without hiding committed-history consequences.</summary>
/// <param name="Status">The mutation disposition.</param>
/// <param name="AffectedCount">The number of entries added, removed, replaced, or finalized.</param>
/// <param name="RecoveryPolicy">The selected recovery policy when a reset is required.</param>
public readonly record struct TranscriptMutationResult(
    TranscriptMutationStatus Status,
    int AffectedCount,
    CommittedHistoryMutationPolicy RecoveryPolicy = CommittedHistoryMutationPolicy.Reject);

/// <summary>Owns the current immutable transcript sequence and its live-entry index.</summary>
public sealed class TranscriptModel
{
    private readonly object _gate = new();
    private TranscriptSequence _entries = TranscriptSequence.Empty;
    private readonly Dictionary<string, int> _entryKeys = new(StringComparer.Ordinal);
    private int _historyEpoch;
    private CommittedHistoryMutationPolicy _historyResetPolicy = CommittedHistoryMutationPolicy.ClearAndReplay;

    /// <summary>Gets the policy associated with the latest explicit committed-history replacement.</summary>
    public CommittedHistoryMutationPolicy HistoryResetPolicy { get { lock (_gate) return _historyResetPolicy; } }

    /// <summary>Releases presentation watermarks when terminal history is explicitly rebuilt.</summary>
    internal void ResetPublication()
    {
        lock (_gate) { _committedCount = 0; _publishedPartialSource = null; _publishedPartialFinal = false; MarkChanged(); }
    }

    private int _committedCount;
    private string? _publishedPartialSource;
    private bool _publishedPartialFinal;
    private int PublishedEntryCount => _committedCount + (_publishedPartialSource is null && !_publishedPartialFinal ? 0 : 1);

    /// <summary>Protects the accepted canonical prefix of the first incompletely published Markdown entry.</summary>
    /// <param name="entryId">Stable identity of the entry following the fully committed prefix.</param>
    /// <param name="sourcePrefix">The immutable accepted canonical source range, starting at offset zero.</param>
    internal void CommitPartialMarkdown(string entryId, string sourcePrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePrefix);
        lock (_gate)
        {
            if (_committedCount >= _entries.Count || _entries[_committedCount].Id != entryId)
                throw new InvalidOperationException("Partial publication must follow the fully committed entry prefix.");
            if (_publishedPartialSource is { } previous && !sourcePrefix.StartsWith(previous, StringComparison.Ordinal))
                throw new InvalidOperationException("Accepted Markdown source is append-only.");
            _publishedPartialSource = sourcePrefix;
            MarkChanged();
        }
    }

    /// <summary>Protects a final entry whose visual rows are being published in bounded batches.</summary>
    /// <param name="entryId">Identity of the final entry immediately after the fully published prefix.</param>
    internal void CommitPartialFinal(string entryId)
    {
        lock (_gate)
        {
            if (_committedCount >= _entries.Count || _entries[_committedCount].Id != entryId ||
                _entries[_committedCount].State != TranscriptEntryState.Final)
                throw new InvalidOperationException("A final continuation must follow the committed prefix.");
            _publishedPartialFinal = true;
            MarkChanged();
        }
    }

    private bool CanUpdatePartial(int index, TranscriptEntry replacement)
    {
        if (index == _committedCount && _publishedPartialFinal) return false;
        if (index != _committedCount || _publishedPartialSource is null) return true;
        var current = _entries[index];
        if (replacement.Id != current.Id || replacement.Metadata != current.Metadata ||
            replacement.VerticalSpacing != current.VerticalSpacing || replacement.Cell.GetType() != current.Cell.GetType())
            return false;
        var document = replacement.Cell switch
        {
            AssistantMessageCell assistant => assistant.Document,
            ReasoningMessageCell reasoning => reasoning.Document,
            _ => null
        };
        var accepted = _entries[index].Cell switch
        {
            AssistantMessageCell assistant => assistant.Document,
            ReasoningMessageCell reasoning => reasoning.Document,
            _ => null
        };
        return document is not null && accepted is not null && document.LineageId == accepted.LineageId &&
            document.Presentation == accepted.Presentation &&
            document.GetCanonicalSource().StartsWith(_publishedPartialSource, StringComparison.Ordinal);
    }

    private int _version;
    private int _updateDepth;
    private bool _updatePending;
    private TranscriptHistoryPresentation _historyPresentation;

    public TranscriptHistoryPresentation HistoryPresentation
    {
        get
        {
            lock (_gate)
            {
                return _historyPresentation;
            }
        }
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            lock (_gate)
            {
                if (_historyPresentation == value)
                    return;

                _historyPresentation = value;
                MarkChanged();
            }
        }
    }

    public IDisposable BeginUpdate()
    {
        lock (_gate)
        {
            _updateDepth++;
        }

        return new UpdateScope(this);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public int HistoryEpoch
    {
        get
        {
            lock (_gate)
            {
                return _historyEpoch;
            }
        }
    }

    public int Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    /// <summary>Gets the number of leading entries irrevocably published to terminal scrollback.</summary>
    public int CommittedCount
    {
        get
        {
            lock (_gate)
            {
                return _committedCount;
            }
        }
    }

    public void AddFinal(TranscriptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            AddEntry(entry.AsFinal());
            MarkChanged();
        }
    }

    /// <summary>Adds or updates a keyed live entry under an explicit committed-history policy.</summary>
    /// <returns>The mutation disposition and required presentation recovery.</returns>
    public TranscriptMutationResult UpsertLive(TranscriptEntry entry, CommittedHistoryMutationPolicy committedHistoryPolicy)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.EntryKey is null)
        {
            throw new ArgumentException("Live transcript entries require an entry key.", nameof(entry));
        }

        lock (_gate)
        {
            if (_entryKeys.TryGetValue(entry.EntryKey, out var index))
            {
                var changesPublished = index < _committedCount || !CanUpdatePartial(index, entry);
                if (changesPublished && committedHistoryPolicy == CommittedHistoryMutationPolicy.Reject)
                    return CannotRetract();
                _entries = _entries.Replace(index, entry.AsLive());
                var reset = ResetCommittedHistoryIfNeeded(changesPublished, committedHistoryPolicy);
                MarkChanged();
                return reset;
            }

            AddEntry(entry.AsLive());
            MarkChanged();
            return Applied();
        }
    }

    /// <summary>Legacy live-entry update using the default clear-and-replay policy.</summary>
    public TranscriptMutationResult UpsertLive(TranscriptEntry entry)
        => UpsertLive(entry, CommittedHistoryMutationPolicy.ClearAndReplay);

    /// <summary>Finalizes or appends a keyed entry under an explicit committed-history policy.</summary>
    /// <returns>The mutation disposition and required presentation recovery.</returns>
    public TranscriptMutationResult FinalizeLive(string entryKey, TranscriptEntry finalEntry, CommittedHistoryMutationPolicy committedHistoryPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);
        ArgumentNullException.ThrowIfNull(finalEntry);

        lock (_gate)
        {
            var committed = finalEntry with { EntryKey = entryKey };
            if (_entryKeys.TryGetValue(entryKey, out var index))
            {
                var changesPublished = index < _committedCount || !CanUpdatePartial(index, committed);
                if (changesPublished && committedHistoryPolicy == CommittedHistoryMutationPolicy.Reject)
                    return CannotRetract();
                _entries = _entries.Replace(index, committed.AsFinal());
                var reset = ResetCommittedHistoryIfNeeded(changesPublished, committedHistoryPolicy);
                MarkChanged();
                return reset;
            }

            AddEntry(committed.AsFinal());
            MarkChanged();
            return Applied();
        }
    }

    /// <summary>Finalizes an existing keyed live entry without appending when it is absent or already final.</summary>
    public bool TryFinalizeLive(string entryKey, TranscriptEntry finalEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);
        ArgumentNullException.ThrowIfNull(finalEntry);

        lock (_gate)
        {
            if (!_entryKeys.TryGetValue(entryKey, out var index)
                || _entries[index].State != TranscriptEntryState.Live)
                return false;

            if (index < _committedCount || !CanUpdatePartial(index, finalEntry)) return false;

            _entries = _entries.Replace(index, (finalEntry with { EntryKey = entryKey }).AsFinal());
            MarkChanged();
            return true;
        }
    }

    public bool RemoveLive(string entryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);

        lock (_gate)
        {
            if (!_entryKeys.TryGetValue(entryKey, out var index))
            {
                return false;
            }

            if (_entries[index].State != TranscriptEntryState.Live)
            {
                return false;
            }

            ThrowIfCommitted(index);

            _entries = TranscriptSequence.Create(_entries.Where((_, candidate) => candidate != index));
            RebuildEntryKeyIndex();
            MarkChanged();
            return true;
        }
    }

    /// <summary>Removes matching entries under an explicit committed-history policy.</summary>
    /// <returns>The mutation disposition and number of removed entries.</returns>
    public TranscriptMutationResult RemoveWhere(Func<TranscriptEntry, bool> predicate, CommittedHistoryMutationPolicy committedHistoryPolicy)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_gate)
        {
            var touchesCommitted = false;
            for (var index = 0; index < PublishedEntryCount; index++)
            {
                if (predicate(_entries[index]))
                {
                    touchesCommitted = true;
                    if (committedHistoryPolicy == CommittedHistoryMutationPolicy.Reject)
                        return CannotRetract();
                }
            }
            var retained = _entries.Where(entry => !predicate(entry)).ToArray();
            var removed = _entries.Count - retained.Length;
            if (removed == 0)
            {
                return Applied(0);
            }

            _entries = TranscriptSequence.Create(retained);
            RebuildEntryKeyIndex();
            var reset = ResetCommittedHistoryIfNeeded(touchesCommitted, committedHistoryPolicy, removed);
            MarkChanged();
            return reset;
        }
    }

    /// <summary>
    /// Replaces every matching entry with one finalized entry at the position of the first match.
    /// If nothing matches, the replacement is appended.
    /// </summary>
    /// <returns>The mutation disposition and number of affected entries.</returns>
    public TranscriptMutationResult ReplaceWhereWith(
        Func<TranscriptEntry, bool> predicate,
        TranscriptEntry replacement,
        CommittedHistoryMutationPolicy committedHistoryPolicy)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(replacement);

        lock (_gate)
        {
            var current = _entries.ToArray();
            var first = Array.FindIndex(current, entry => predicate(entry));
            var touchesCommitted = first >= 0 && first < PublishedEntryCount;
            if (touchesCommitted && committedHistoryPolicy == CommittedHistoryMutationPolicy.Reject)
                return CannotRetract();
            var retained = current.Where(entry => !predicate(entry)).ToList();
            var removed = current.Length - retained.Count;
            retained.Insert(first < 0 ? retained.Count : Math.Min(first, retained.Count), replacement.AsFinal());
            _entries = TranscriptSequence.Create(retained);
            RebuildEntryKeyIndex();
            var reset = ResetCommittedHistoryIfNeeded(touchesCommitted, committedHistoryPolicy, Math.Max(1, removed));
            MarkChanged();
            return reset;
        }
    }

    /// <summary>Clears the transcript under an explicit committed-history policy.</summary>
    /// <returns>The mutation disposition and number of cleared entries.</returns>
    public TranscriptMutationResult ClearAll(CommittedHistoryMutationPolicy committedHistoryPolicy)
    {
        lock (_gate)
        {
            if (PublishedEntryCount != 0 && committedHistoryPolicy == CommittedHistoryMutationPolicy.Reject)
                return CannotRetract();
            var count = _entries.Count;
            var hadCommitted = PublishedEntryCount != 0;
            _entries = TranscriptSequence.Empty;
            _entryKeys.Clear();
            _committedCount = 0;
            _publishedPartialSource = null; _publishedPartialFinal = false;
            _historyResetPolicy = committedHistoryPolicy;
            _historyEpoch++;
            MarkChanged();
            return hadCommitted ? RequiresReset(count, committedHistoryPolicy) : Applied(count);
        }
    }

    /// <summary>
    /// Atomically replaces all visible transcript history with one finalized entry.
    /// </summary>
    /// <remarks>
    /// This is the boundary primitive for checkpoints that supersede every entry
    /// rendered before them, independent of the event or cell types involved.
    /// </remarks>
    /// <returns>The mutation disposition and required presentation recovery.</returns>
    public TranscriptMutationResult ReplaceHistoryWith(TranscriptEntry replacement, CommittedHistoryMutationPolicy committedHistoryPolicy)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        lock (_gate)
        {
            if (PublishedEntryCount != 0 && committedHistoryPolicy == CommittedHistoryMutationPolicy.Reject)
                return CannotRetract();
            var hadCommitted = PublishedEntryCount != 0;
            _entries = TranscriptSequence.Empty;
            _entryKeys.Clear();
            _committedCount = 0;
            _publishedPartialSource = null; _publishedPartialFinal = false;
            AddEntry(replacement.AsFinal());
            _historyResetPolicy = committedHistoryPolicy;
            _historyEpoch++;
            MarkChanged();
            return hadCommitted ? RequiresReset(1, committedHistoryPolicy) : Applied();
        }
    }

    public TranscriptEntry GetEntry(int index)
    {
        lock (_gate)
        {
            return _entries[index];
        }
    }

    /// <summary>Captures the current transcript revision without copying unchanged entry storage.</summary>
    public TranscriptSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new TranscriptSnapshot(_entries, _version, _historyEpoch, _committedCount);
        }
    }

    /// <summary>Captures entries matching <paramref name="predicate"/> in a new immutable sequence.</summary>
    /// <param name="predicate">Selects entries to include.</param>
    /// <returns>An immutable filtered snapshot.</returns>
    public TranscriptSnapshot Snapshot(Func<TranscriptEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_gate)
        {
            return new TranscriptSnapshot(
                TranscriptSequence.Create(_entries.Where(predicate)),
                _version,
                _historyEpoch,
                CommittedCount: 0);
        }
    }

    /// <summary>Advances the committed prefix after a complete scrollback publication.</summary>
    /// <param name="expectedStart">The expected current committed-entry count.</param>
    /// <param name="count">The number of additional entries accepted by the terminal.</param>
    public void CommitPrefix(int expectedStart, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedStart);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        lock (_gate)
        {
            if (_committedCount != expectedStart)
                throw new InvalidOperationException("The transcript commit watermark changed before publication completed.");
            if (count > _entries.Count - _committedCount)
                throw new ArgumentOutOfRangeException(nameof(count));
            for (var index = _committedCount; index < _committedCount + count; index++)
            {
                var entry = _entries[index];
                if (entry.State != TranscriptEntryState.Final || entry.CommitPolicy == TranscriptCommitPolicy.Never)
                    throw new InvalidOperationException("Only a contiguous publishable final prefix can be committed.");
            }
            if (count > 0) { _publishedPartialSource = null; _publishedPartialFinal = false; }
            _committedCount += count;
            MarkChanged();
        }
    }

    private void AddEntry(TranscriptEntry entry)
    {
        _entries = _entries.Append(entry);
        if (entry.EntryKey is not null)
        {
            _entryKeys[entry.EntryKey] = _entries.Count - 1;
        }
    }

    private TranscriptMutationResult ResetCommittedHistoryIfNeeded(
        bool touchesCommitted,
        CommittedHistoryMutationPolicy policy,
        int affectedCount = 1)
    {
        if (!touchesCommitted) return Applied(affectedCount);
        _committedCount = 0;
        _publishedPartialSource = null; _publishedPartialFinal = false;
        _historyResetPolicy = policy;
        _historyEpoch++;
        return RequiresReset(affectedCount, policy);
    }

    private static TranscriptMutationResult Applied(int count = 1)
        => new(TranscriptMutationStatus.Applied, count);

    private static TranscriptMutationResult CannotRetract()
        => new(TranscriptMutationStatus.CannotRetract, 0);

    private static TranscriptMutationResult RequiresReset(int count, CommittedHistoryMutationPolicy policy)
        => new(TranscriptMutationStatus.RequiresPresentationReset, count, policy);

    private void RebuildEntryKeyIndex()
    {
        _entryKeys.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].EntryKey is { } key)
            {
                _entryKeys[key] = i;
            }
        }
    }

    private void MarkChanged()
    {
        if (_updateDepth > 0)
        {
            _updatePending = true;
            return;
        }

        _version++;
    }

    private void ThrowIfCommitted(int index)
    {
        if (index < PublishedEntryCount)
            throw new InvalidOperationException("Committed terminal scrollback entries are immutable.");
    }

    private void EndUpdate()
    {
        lock (_gate)
        {
            if (_updateDepth == 0)
            {
                throw new InvalidOperationException("Transcript update scope was already completed.");
            }

            _updateDepth--;
            if (_updateDepth == 0 && _updatePending)
            {
                _updatePending = false;
                _version++;
            }
        }
    }

    private sealed class UpdateScope(TranscriptModel owner) : IDisposable
    {
        private TranscriptModel? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.EndUpdate();
    }

}

/// <summary>Captures one immutable transcript model revision.</summary>
/// <param name="Entries">The persistent indexed entries in this revision.</param>
/// <param name="Version">The model version captured by the snapshot.</param>
/// <param name="HistoryEpoch">The presentation epoch captured by the snapshot.</param>
public sealed record TranscriptSnapshot(
    TranscriptSequence Entries,
    int Version,
    int HistoryEpoch,
    int CommittedCount);
