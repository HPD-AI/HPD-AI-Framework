using HPD.Agent;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Models;

public sealed class WidgetSlotModel
{
    private readonly object _gate = new();
    private readonly List<WidgetSlotEntry> _entries = [];
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

    public void Add(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        lock (_gate)
        {
            _entries.Add(new WidgetSlotEntry(null, component, null));
            _version++;
        }
    }

    public void Set(string key, IComponent? component, HpdContributionOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            var index = _entries.FindIndex(entry =>
                string.Equals(entry.Key, key, StringComparison.Ordinal) &&
                entry.Owner == owner);

            if (component is null)
            {
                if (index >= 0)
                {
                    _entries.RemoveAt(index);
                    _version++;
                }

                return;
            }

            var entry = new WidgetSlotEntry(key, component, owner);
            if (index >= 0)
            {
                _entries[index] = entry;
            }
            else
            {
                _entries.Add(entry);
            }

            _version++;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _version++;
        }
    }

    public bool ClearOwner(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            var removed = false;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Owner == owner)
                {
                    _entries.RemoveAt(i);
                    removed = true;
                }
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
            var removed = false;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Owner is not null)
                {
                    _entries.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
            {
                _version++;
            }

            return removed;
        }
    }

    public bool Remove(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        lock (_gate)
        {
            var index = _entries.FindIndex(entry => ReferenceEquals(entry.Component, component));
            if (index < 0)
            {
                return false;
            }

            _entries.RemoveAt(index);
            _version++;
            return true;
        }
    }

    public IComponent GetComponent(int index)
    {
        lock (_gate)
        {
            return _entries[index].Component;
        }
    }

    public void CopyTo(List<IComponent> target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate)
        {
            target.Clear();
            target.AddRange(_entries.Select(static entry => entry.Component));
        }
    }

    public IReadOnlyList<WidgetSlotEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }
}

public sealed record WidgetSlotEntry(
    string? Key,
    IComponent Component,
    HpdContributionOwner? Owner);
