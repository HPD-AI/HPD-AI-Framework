using HPD.Agent;

namespace HPD.Agent.TUI.Models;

public sealed class SessionStatusModel
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionStatusEntry> _entries = new(StringComparer.Ordinal);
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

    public void Set(string key, string? text, HpdContributionOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                if (_entries.Remove(key))
                {
                    _version++;
                }

                return;
            }

            _entries[key] = new SessionStatusEntry(key, text, owner);
            _version++;
        }
    }

    public bool ClearOwner(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            var removed = false;
            foreach (var key in _entries
                         .Where(pair => pair.Value.Owner == owner)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                removed |= _entries.Remove(key);
            }

            if (removed)
            {
                _version++;
            }

            return removed;
        }
    }

    public bool ClearOwned()
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return false;
            }

            _entries.Clear();
            _version++;
            return true;
        }
    }

    public IReadOnlyList<SessionStatusEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Values
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .ToArray();
        }
    }
}

public sealed record SessionStatusEntry(
    string Key,
    string Text,
    HpdContributionOwner Owner);
