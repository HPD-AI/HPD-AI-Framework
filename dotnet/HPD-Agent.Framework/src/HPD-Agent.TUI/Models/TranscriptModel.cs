namespace HPD.Agent.TUI.Models;

/// <summary>Owns the current immutable transcript sequence and its live-entry index.</summary>
public sealed class TranscriptModel
{
    private readonly object _gate = new();
    private TranscriptSequence _entries = TranscriptSequence.Empty;
    private readonly Dictionary<string, int> _entryKeys = new(StringComparer.Ordinal);
    private int _historyEpoch;
    private int _committedCount;
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

    public void UpsertLive(TranscriptEntry entry)
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
                ThrowIfCommitted(index);
                _entries = _entries.Replace(index, entry.AsLive());
                MarkChanged();
                return;
            }

            AddEntry(entry.AsLive());
            MarkChanged();
        }
    }

    public void FinalizeLive(string entryKey, TranscriptEntry finalEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);
        ArgumentNullException.ThrowIfNull(finalEntry);

        lock (_gate)
        {
            var committed = finalEntry with { EntryKey = entryKey };
            if (_entryKeys.TryGetValue(entryKey, out var index))
            {
                ThrowIfCommitted(index);
                _entries = _entries.Replace(index, committed.AsFinal());
                MarkChanged();
                return;
            }

            AddEntry(committed.AsFinal());
            MarkChanged();
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

            ThrowIfCommitted(index);

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

    public int RemoveWhere(Func<TranscriptEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_gate)
        {
            for (var index = 0; index < _committedCount; index++)
            {
                if (predicate(_entries[index]))
                    throw new InvalidOperationException("Committed terminal scrollback entries cannot be removed.");
            }
            var retained = _entries.Where(entry => !predicate(entry)).ToArray();
            var removed = _entries.Count - retained.Length;
            if (removed == 0)
            {
                return 0;
            }

            _entries = TranscriptSequence.Create(retained);
            RebuildEntryKeyIndex();
            MarkChanged();
            return removed;
        }
    }

    /// <summary>
    /// Replaces every matching entry with one finalized entry at the position of the first match.
    /// If nothing matches, the replacement is appended.
    /// </summary>
    public int ReplaceWhereWith(
        Func<TranscriptEntry, bool> predicate,
        TranscriptEntry replacement)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(replacement);

        lock (_gate)
        {
            var current = _entries.ToArray();
            var first = Array.FindIndex(current, entry => predicate(entry));
            if (first >= 0 && first < _committedCount)
                throw new InvalidOperationException("Committed terminal scrollback entries cannot be replaced.");
            var retained = current.Where(entry => !predicate(entry)).ToList();
            var removed = current.Length - retained.Count;
            retained.Insert(first < 0 ? retained.Count : Math.Min(first, retained.Count), replacement.AsFinal());
            _entries = TranscriptSequence.Create(retained);
            RebuildEntryKeyIndex();
            MarkChanged();
            return removed;
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            if (_committedCount != 0)
                throw new InvalidOperationException("Committed terminal scrollback cannot be cleared in the current presentation epoch.");
            _entries = TranscriptSequence.Empty;
            _entryKeys.Clear();
            _committedCount = 0;
            _historyEpoch++;
            MarkChanged();
        }
    }

    /// <summary>
    /// Atomically replaces all visible transcript history with one finalized entry.
    /// </summary>
    /// <remarks>
    /// This is the boundary primitive for checkpoints that supersede every entry
    /// rendered before them, independent of the event or cell types involved.
    /// </remarks>
    public void ReplaceHistoryWith(TranscriptEntry replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        lock (_gate)
        {
            if (_committedCount != 0)
                throw new InvalidOperationException("Committed terminal scrollback cannot be replaced in the current presentation epoch.");
            _entries = TranscriptSequence.Empty;
            _entryKeys.Clear();
            _committedCount = 0;
            AddEntry(replacement.AsFinal());
            _historyEpoch++;
            MarkChanged();
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
        if (index < _committedCount)
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
