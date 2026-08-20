using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HPD.Agent.History.Abi;
namespace HPD.Agent.History.Native;
[StructLayout(LayoutKind.Sequential)]internal unsafe struct BytesV1{internal uint AbiSize,AbiVersion;internal byte* Ptr;internal uint Len,Reserved;}
[StructLayout(LayoutKind.Sequential)]internal unsafe struct OutputV1{internal uint AbiSize,AbiVersion;internal byte* Ptr;internal uint Capacity,Written,Required;internal ulong Cursor;}
[StructLayout(LayoutKind.Sequential)]internal unsafe struct ErrorV1{internal uint AbiSize,AbiVersion;internal int Code;internal uint DetailLen;internal byte* Detail;internal ulong Reserved;}
internal static unsafe class NativeExportsV1
{
    private static readonly HistoryAbiEngineV1 Engine=new();
    [UnmanagedCallersOnly(EntryPoint="hpd_history_abi_version",CallConvs=[typeof(CallConvCdecl)])]internal static uint Version()=>HistoryAbiEngineV1.AbiVersion;
    [UnmanagedCallersOnly(EntryPoint="hpd_history_query_open",CallConvs=[typeof(CallConvCdecl)])]internal static int QueryOpen(BytesV1* r,ulong* h,ErrorV1* e)=>Open(HistoryHandleKindV1.Query,r,h,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_query_next",CallConvs=[typeof(CallConvCdecl)])]internal static int QueryNext(ulong h,OutputV1* o,ErrorV1* e)=>Next(h,HistoryHandleKindV1.Query,o,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_query_close",CallConvs=[typeof(CallConvCdecl)])]internal static int QueryClose(ulong h,ErrorV1* e)=>Close(h,HistoryHandleKindV1.Query,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_subscription_open",CallConvs=[typeof(CallConvCdecl)])]internal static int SubscriptionOpen(BytesV1* r,ulong* h,ErrorV1* e)=>Open(HistoryHandleKindV1.Subscription,r,h,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_subscription_next",CallConvs=[typeof(CallConvCdecl)])]internal static int SubscriptionNext(ulong h,uint timeout,OutputV1* o,ErrorV1* e)=>timeout==0?14:Next(h,HistoryHandleKindV1.Subscription,o,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_subscription_ack",CallConvs=[typeof(CallConvCdecl)])]internal static int SubscriptionAck(ulong h,ulong cursor,ErrorV1* e)=>Finish(Engine.Ack(h,cursor),e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_subscription_close",CallConvs=[typeof(CallConvCdecl)])]internal static int SubscriptionClose(ulong h,ErrorV1* e)=>Close(h,HistoryHandleKindV1.Subscription,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_export_start",CallConvs=[typeof(CallConvCdecl)])]internal static int ExportStart(BytesV1* r,ulong* h,ErrorV1* e)=>Open(HistoryHandleKindV1.Export,r,h,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_export_status",CallConvs=[typeof(CallConvCdecl)])]internal static int ExportStatus(ulong h,OutputV1* o,ErrorV1* e)=>Status(h,HistoryHandleKindV1.Export,o,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_export_cancel",CallConvs=[typeof(CallConvCdecl)])]internal static int ExportCancel(ulong h,ErrorV1* e)=>Finish(Engine.Cancel(h,HistoryHandleKindV1.Export),e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_export_content_open",CallConvs=[typeof(CallConvCdecl)])]internal static int ExportContentOpen(ulong h,ulong* c,ErrorV1* e)=>c is null?14:Finish(Engine.OpenContent(h,out *c),e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_export_content_next",CallConvs=[typeof(CallConvCdecl)])]internal static int ExportContentNext(ulong h,OutputV1* o,ErrorV1* e)=>Next(h,HistoryHandleKindV1.Content,o,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_history_export_content_close",CallConvs=[typeof(CallConvCdecl)])]internal static int ExportContentClose(ulong h,ErrorV1* e)=>Close(h,HistoryHandleKindV1.Content,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_privacy_delete_start",CallConvs=[typeof(CallConvCdecl)])]internal static int DeleteStart(BytesV1* r,ulong* h,ErrorV1* e)=>Open(HistoryHandleKindV1.Privacy,r,h,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_privacy_hold_start",CallConvs=[typeof(CallConvCdecl)])]internal static int HoldStart(BytesV1* r,ulong* h,ErrorV1* e)=>Open(HistoryHandleKindV1.Hold,r,h,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_privacy_hold_release",CallConvs=[typeof(CallConvCdecl)])]internal static int HoldRelease(ulong h,BytesV1* r,ErrorV1* e)=>Finish(Engine.ReleaseHold(h,Input(r)),e);
    [UnmanagedCallersOnly(EntryPoint="hpd_privacy_status",CallConvs=[typeof(CallConvCdecl)])]internal static int PrivacyStatus(ulong h,OutputV1* o,ErrorV1* e)=>Status(h,HistoryHandleKindV1.Privacy,o,e);
    [UnmanagedCallersOnly(EntryPoint="hpd_privacy_cancel",CallConvs=[typeof(CallConvCdecl)])]internal static int PrivacyCancel(ulong h,ErrorV1* e)=>Finish(Engine.Cancel(h,HistoryHandleKindV1.Privacy),e);
    [UnmanagedCallersOnly(EntryPoint="hpd_buffer_free",CallConvs=[typeof(CallConvCdecl)])]internal static void BufferFree(void* p,uint len){if(p is not null&&len>0)NativeMemory.Free(p);}
    [UnmanagedCallersOnly(EntryPoint="hpd_error_free",CallConvs=[typeof(CallConvCdecl)])]internal static void ErrorFree(ErrorV1* e){if(e is null)return;if(e->Detail is not null)NativeMemory.Free(e->Detail);*e=default;}
    private static int Open(HistoryHandleKindV1 kind,BytesV1* r,ulong* h,ErrorV1* e){if(h is null)return Finish(HistoryApiStatusV1.InvalidArgument,e);var s=Engine.Open(kind,Input(r),out var value);if(s==0)*h=value;return Finish(s,e);}
    private static int Next(ulong h,HistoryHandleKindV1 kind,OutputV1* o,ErrorV1* e){if(!Valid(o))return Finish(HistoryApiStatusV1.InvalidArgument,e);var original=o->Cursor;var s=Engine.Next(h,kind,o->Capacity,out var chunk,out var cursor,out var required);o->Written=0;o->Required=required;if(s==0){chunk.CopyTo(new Span<byte>(o->Ptr,chunk.Length));o->Written=(uint)chunk.Length;o->Cursor=cursor;}else o->Cursor=original;return Finish(s,e);}
    private static int Status(ulong h,HistoryHandleKindV1 kind,OutputV1* o,ErrorV1* e){if(!Valid(o))return Finish(HistoryApiStatusV1.InvalidArgument,e);var s=Engine.Status(h,kind,out var bytes);o->Written=0;o->Required=0;if(s==0&&o->Capacity<(uint)bytes.Length){o->Required=(uint)bytes.Length;return Finish(HistoryApiStatusV1.BufferTooSmall,e);}if(s==0){bytes.CopyTo(new Span<byte>(o->Ptr,bytes.Length));o->Written=(uint)bytes.Length;}return Finish(s,e);}
    private static int Close(ulong h,HistoryHandleKindV1 kind,ErrorV1* e)=>Finish(Engine.Close(h,kind),e);
    private static ReadOnlySpan<byte> Input(BytesV1* r)=>r is null||r->AbiSize!=24||r->AbiVersion!=HistoryAbiEngineV1.AbiVersion||r->Reserved!=0||r->Ptr is null||r->Len==0||r->Len>HistoryAbiEngineV1.MaximumRequestBytes?[]:new(r->Ptr,(int)r->Len);
    private static bool Valid(OutputV1* o)=>o is not null&&o->AbiSize==40&&o->AbiVersion==HistoryAbiEngineV1.AbiVersion&&o->Ptr is not null&&o->Capacity>0;
    private static int Finish(HistoryApiStatusV1 s,ErrorV1* e){if(e is not null){if(e->AbiSize!=32||e->AbiVersion!=HistoryAbiEngineV1.AbiVersion||e->Reserved!=0)return 14;e->Code=(int)s;e->DetailLen=0;e->Detail=null;}return(int)s;}
}
