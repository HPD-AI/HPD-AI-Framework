using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HPD.Agent.Audio.Transport.Abi;

internal static class NativeExportsV1
{
    private static readonly TransportLifecycleAbiV1 Lifecycle = new();

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_v1_create", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Create(ulong session, ulong generation) => Lifecycle.Create(session, generation);

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_v1_bind", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Bind(int handle, ulong session, ulong generation) => Lifecycle.Bind(handle, session, generation);

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_v1_start", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Start(int handle, ulong session, ulong generation) => Lifecycle.Start(handle, session, generation);

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_v1_stop", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Stop(int handle, ulong session, ulong generation) => Lifecycle.Stop(handle, session, generation);

    [UnmanagedCallersOnly(EntryPoint = "hpd_audio_transport_v1_destroy", CallConvs = [typeof(CallConvCdecl)])]
    internal static int Destroy(int handle, ulong session, ulong generation) => Lifecycle.Destroy(handle, session, generation);
}
