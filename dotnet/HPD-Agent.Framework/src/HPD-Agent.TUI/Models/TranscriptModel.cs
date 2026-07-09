namespace HPD.Agent.TUI.Models;

public sealed class TranscriptModel
{
    private readonly object _gate = new();
    private readonly List<TranscriptEntry> _entries = [];
    private readonly Dictionary<string, int> _entryKeys = new(StringComparer.Ordinal);
    private int _historyEpoch;
    private int _version;

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
            _version++;
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
                _version++;
                return;
            }

            AddEntry(entry.AsLive());
            _version++;
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
                _version++;
                return;
            }

            AddEntry(committed.AsFinal());
            _version++;
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
            _version++;
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
            _version++;
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
            _version++;
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

}

public sealed record TranscriptSnapshot(
    IReadOnlyList<TranscriptEntry> Entries,
    int Version,
    int HistoryEpoch);
