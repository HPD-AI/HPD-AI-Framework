using HPD.TUI.Core;

namespace HPD.Agent.TUI.Models;

public sealed class WidgetSlotModel
{
    private readonly object _gate = new();
    private readonly List<IComponent> _components = [];
    private int _version;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _components.Count;
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
            _components.Add(component);
            _version++;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _components.Clear();
            _version++;
        }
    }

    public bool Remove(IComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        lock (_gate)
        {
            if (!_components.Remove(component))
                return false;

            _version++;
            return true;
        }
    }

    public IComponent GetComponent(int index)
    {
        lock (_gate)
        {
            return _components[index];
        }
    }

    public void CopyTo(List<IComponent> target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_gate)
        {
            target.Clear();
            target.AddRange(_components);
        }
    }
}
