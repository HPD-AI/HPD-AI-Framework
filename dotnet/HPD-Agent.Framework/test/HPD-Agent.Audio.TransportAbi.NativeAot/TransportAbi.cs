using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class TransportAbi
{
    private const int MaximumSlots = 64;
    private static readonly Slot[] Slots = new Slot[MaximumSlots];
    private static readonly object Gate = new();

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_create", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Create(ulong session, ulong generation)
    {
        if (session == 0 || generation == 0) return -1;
        lock (Gate)
        {
            for (var index = 0; index < Slots.Length; index++)
            {
                if (Slots[index].State != 0) continue;
                Slots[index] = new Slot(session, generation, 1);
                return index + 1;
            }
        }
        return -2;
    }

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_bind", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Bind(int handle, ulong session, ulong generation) => Transition(handle, session, generation, 1, 2);

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_start", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Start(int handle, ulong session, ulong generation) => Transition(handle, session, generation, 2, 3);

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_stop", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Stop(int handle, ulong session, ulong generation) => Transition(handle, session, generation, 3, 4);

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_destroy", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Destroy(int handle, ulong session, ulong generation)
    {
        lock (Gate)
        {
            if (!TryGet(handle, out var index, out var slot)) return -3;
            if (slot.Session != session || slot.Generation != generation) return -4;
            Slots[index] = default;
            return 0;
        }
    }

    private static int Transition(int handle, ulong session, ulong generation, byte expected, byte next)
    {
        lock (Gate)
        {
            if (!TryGet(handle, out var index, out var slot)) return -3;
            if (slot.Session != session || slot.Generation != generation) return -4;
            if (slot.State != expected) return -5;
            Slots[index] = slot with { State = next };
            return 0;
        }
    }

    private static bool TryGet(int handle, out int index, out Slot slot)
    {
        index = handle - 1;
        if ((uint)index >= MaximumSlots) { slot = default; return false; }
        slot = Slots[index];
        return slot.State != 0;
    }

    private readonly record struct Slot(ulong Session, ulong Generation, byte State);
}
