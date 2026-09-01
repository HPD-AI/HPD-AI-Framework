namespace HPD.Agent.TUI.Models;

public sealed class TranscriptModel
{
    private readonly object _gate = new();
    private readonly List<TranscriptEntry> _entries = [];
    private readonly Dictionary<string, int> _entryKeys = new(StringComparer.Ordinal);
    private int _historyEpoch;
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
                _entries[index] = entry.AsLive();
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
                _entries[index] = committed.AsFinal();
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

            _entries[index] = (finalEntry with { EntryKey = entryKey }).AsFinal();
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

            _entries.RemoveAt(index);
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
            var removed = _entries.RemoveAll(entry => predicate(entry));
            if (removed == 0)
            {
                return 0;
            }

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
            var first = _entries.FindIndex(entry => predicate(entry));
            var removed = _entries.RemoveAll(entry => predicate(entry));
            _entries.Insert(first < 0 ? _entries.Count : first, replacement.AsFinal());
            RebuildEntryKeyIndex();
            MarkChanged();
            return removed;
        }
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            _entries.Clear();
            _entryKeys.Clear();
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
            _entries.Clear();
            _entryKeys.Clear();
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

    public TranscriptSnapshot Snapshot()
        => Snapshot(entry => true);

    public TranscriptSnapshot Snapshot(Func<TranscriptEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_gate)
        {
            return new TranscriptSnapshot(
                _entries.Where(predicate).ToArray(),
                _version,
                _historyEpoch);
        }
    }

    private void AddEntry(TranscriptEntry entry)
    {
        _entries.Add(entry);
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

public sealed record TranscriptSnapshot(
    IReadOnlyList<TranscriptEntry> Entries,
    int Version,
    int HistoryEpoch);
