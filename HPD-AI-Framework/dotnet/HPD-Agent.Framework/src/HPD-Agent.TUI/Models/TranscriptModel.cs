namespace HPD.Agent.TUI.Models;

public sealed class TranscriptModel
{
    private readonly object _gate = new();
    private readonly List<TranscriptEntry> _entries = [];
    private readonly Dictionary<string, int> _entryKeys = new(StringComparer.Ordinal);
    private int _viewOffsetRowsFromBottom;
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

    public int ViewOffsetRowsFromBottom
    {
        get
        {
            lock (_gate)
            {
                return _viewOffsetRowsFromBottom;
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

    public void Append(TranscriptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            var wasPinnedToBottom = _viewOffsetRowsFromBottom == 0;
            _entries.Add(entry);
            _version++;
            if (entry.EntryKey is not null)
            {
                _entryKeys[entry.EntryKey] = _entries.Count - 1;
            }

            _viewOffsetRowsFromBottom = wasPinnedToBottom ? 0 : ClampViewOffset(_viewOffsetRowsFromBottom + 1);
        }
    }

    public void Update(TranscriptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.EntryKey is null)
        {
            Append(entry);
            return;
        }

        lock (_gate)
        {
            if (_entryKeys.TryGetValue(entry.EntryKey, out var index))
            {
                _entries[index] = entry;
                _version++;
                return;
            }

            var wasPinnedToBottom = _viewOffsetRowsFromBottom == 0;
            _entries.Add(entry);
            _entryKeys[entry.EntryKey] = _entries.Count - 1;
            _version++;
            _viewOffsetRowsFromBottom = wasPinnedToBottom ? 0 : ClampViewOffset(_viewOffsetRowsFromBottom + 1);
        }
    }

    public bool Remove(string entryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);

        lock (_gate)
        {
            if (!_entryKeys.TryGetValue(entryKey, out var index))
            {
                return false;
            }

            _entries.RemoveAt(index);
            RebuildEntryKeyIndex();
            _version++;
            _viewOffsetRowsFromBottom = ClampViewOffset(_viewOffsetRowsFromBottom);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _entryKeys.Clear();
            _version++;
            _viewOffsetRowsFromBottom = 0;
        }
    }

    public void ScrollUp(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);

        lock (_gate)
        {
            _viewOffsetRowsFromBottom = ClampViewOffset(_viewOffsetRowsFromBottom + rows);
        }
    }

    public void ScrollDown(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);

        lock (_gate)
        {
            _viewOffsetRowsFromBottom = ClampViewOffset(_viewOffsetRowsFromBottom - rows);
        }
    }

    public void ScrollToTop()
    {
        lock (_gate)
        {
            _viewOffsetRowsFromBottom = ClampViewOffset(int.MaxValue);
        }
    }

    public void ScrollToBottom()
    {
        lock (_gate)
        {
            _viewOffsetRowsFromBottom = 0;
        }
    }

    public TranscriptEntry GetEntry(int index)
    {
        lock (_gate)
        {
            return _entries[index];
        }
    }

    public void CopyTo(List<TranscriptEntry> target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate)
        {
            target.Clear();
            target.AddRange(_entries);
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

    private static int ClampViewOffset(int value)
        => Math.Clamp(value, 0, 1_000_000);
}
