namespace HPD.Agent.Audio.Transport.Abi;

/// <summary>Generation-fenced managed owner behind the isolated transport ABI.</summary>
public sealed class TransportLifecycleAbiV1
{
    private const int MaximumSlots = 64;
    private readonly Slot[] _slots = new Slot[MaximumSlots];
    private readonly object _gate = new();

    public int Create(ulong session, ulong generation)
    {
        if (session == 0 || generation == 0) return -1;
        lock (_gate)
        {
            for (var index = 0; index < _slots.Length; index++)
            {
                if (_slots[index].State != 0) continue;
                _slots[index] = new Slot(session, generation, 1);
                return index + 1;
            }
        }
        return -2;
    }

    public int Bind(int handle, ulong session, ulong generation) => Transition(handle, session, generation, 1, 2);
    public int Start(int handle, ulong session, ulong generation) => Transition(handle, session, generation, 2, 3);
    public int Stop(int handle, ulong session, ulong generation) => Transition(handle, session, generation, 3, 4);

    public int Destroy(int handle, ulong session, ulong generation)
    {
        lock (_gate)
        {
            if (!TryGet(handle, out var index, out var slot)) return -3;
            if (slot.Session != session || slot.Generation != generation) return -4;
            _slots[index] = default;
            return 0;
        }
    }

    private int Transition(int handle, ulong session, ulong generation, byte expected, byte next)
    {
        lock (_gate)
        {
            if (!TryGet(handle, out var index, out var slot)) return -3;
            if (slot.Session != session || slot.Generation != generation) return -4;
            if (slot.State != expected) return -5;
            _slots[index] = slot with { State = next };
            return 0;
        }
    }

    private bool TryGet(int handle, out int index, out Slot slot)
    {
        index = handle - 1;
        if ((uint)index >= MaximumSlots) { slot = default; return false; }
        slot = _slots[index];
        return slot.State != 0;
    }

    private readonly record struct Slot(ulong Session, ulong Generation, byte State);
}
