using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HPD.Agent.Replay.Abi;

namespace HPD.Agent.Replay.Native;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct OwnedBytesV1 { internal byte* Ptr; internal ulong Len; internal ulong Cap; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ResultV1 { internal int Code; internal OwnedBytesV1 Payload; internal OwnedBytesV1 Error; }

internal static unsafe class NativeExportsV1
{
    private static readonly ReplayAbiEngineV1 Engine = new();

    [UnmanagedCallersOnly(EntryPoint="hpd_replay_v1_open",CallConvs=[typeof(CallConvCdecl)])]
    internal static int Open(byte* request,ulong length,ulong* output)
    { if(output is null)return 14;var status=Engine.Open(Input(request,length),out var handle);if(status==0)*output=handle;return(int)status; }
    [UnmanagedCallersOnly(EntryPoint="hpd_replay_v1_advance",CallConvs=[typeof(CallConvCdecl)])]
    internal static int Advance(ulong handle,byte* request,ulong length)=>(int)Engine.Advance(handle,Input(request,length));
    [UnmanagedCallersOnly(EntryPoint="hpd_replay_v1_step",CallConvs=[typeof(CallConvCdecl)])]
    internal static int Step(ulong handle,byte* request,ulong length)=>(int)Engine.Step(handle,Input(request,length));
    [UnmanagedCallersOnly(EntryPoint="hpd_replay_v1_explore",CallConvs=[typeof(CallConvCdecl)])]
    internal static int Explore(ulong handle,byte* request,ulong length,ResultV1* output)
    {var status=Engine.Explore(handle,Input(request,length),out var payload);return Write(status,payload,output);}
    [UnmanagedCallersOnly(EntryPoint="hpd_replay_v1_status",CallConvs=[typeof(CallConvCdecl)])]
    internal static int Status(ulong handle,ResultV1* output)
    {var status=Engine.Status(handle,out var payload);return Write(status,payload,output);}
    [UnmanagedCallersOnly(EntryPoint="hpd_replay_v1_complete",CallConvs=[typeof(CallConvCdecl)])]
    internal static int Complete(ulong handle,ResultV1* output)
    {var status=Engine.Complete(handle,out var payload);return Write(status,payload,output);}
    [UnmanagedCallersOnly(EntryPoint="hpd_replay_v1_close",CallConvs=[typeof(CallConvCdecl)])]
    internal static int Close(ulong handle)=>(int)Engine.Close(handle);
    [UnmanagedCallersOnly(EntryPoint="hpd_replay_v1_free",CallConvs=[typeof(CallConvCdecl)])]
    internal static void Free(OwnedBytesV1* bytes)
    {if(bytes is null)return;if(bytes->Ptr is not null)NativeMemory.Free(bytes->Ptr);*bytes=default;}

    private static ReadOnlySpan<byte> Input(byte* request,ulong length)
    {if(request is null||length==0||length>ReplayAbiEngineV1.MaximumOperationBytes)return [];return new(request,checked((int)length));}
    private static int Write(ReplayAbiStatusV1 status,byte[] payload,ResultV1* output)
    {if(output is null)return 14;*output=default;output->Code=(int)status;if(status!=ReplayAbiStatusV1.Ok)return(int)status;var ptr=(byte*)NativeMemory.Alloc((nuint)payload.Length);payload.CopyTo(new Span<byte>(ptr,payload.Length));output->Payload=new(){Ptr=ptr,Len=(ulong)payload.Length,Cap=(ulong)payload.Length};return 0;}
}
