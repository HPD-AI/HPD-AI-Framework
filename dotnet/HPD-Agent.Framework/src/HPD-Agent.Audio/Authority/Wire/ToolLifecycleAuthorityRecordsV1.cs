using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract record ToolLifecycleRecordV1
{
    protected ToolLifecycleRecordV1(OperationId operationId, JournalPositionV1 sourcePosition, ExpectedAuthorityVectorV1 authority, ushort disposition)
    { if(!operationId.IsValid||!sourcePosition.IsValid||authority is null||authority.Session!=sourcePosition.Session||disposition==0) throw new ArgumentException("Invalid tool lifecycle record."); OperationId=operationId;SourcePosition=sourcePosition;Authority=authority;Disposition=disposition; }
    internal OperationId OperationId {get;} internal JournalPositionV1 SourcePosition {get;} internal ExpectedAuthorityVectorV1 Authority {get;} internal ushort Disposition {get;}
}

internal sealed record ToolControlRecordedV1 : ToolLifecycleRecordV1 { internal ToolControlRecordedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolArgumentsFinalizedV1 : ToolLifecycleRecordV1 { internal ToolArgumentsFinalizedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolApprovalDecidedV1 : ToolLifecycleRecordV1 { internal ToolApprovalDecidedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolDispositionChosenV1 : ToolLifecycleRecordV1 { internal ToolDispositionChosenV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolOwnerClaimedV1 : ToolLifecycleRecordV1 { internal ToolOwnerClaimedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolDispatchAuthorizedV1 : ToolLifecycleRecordV1 { internal ToolDispatchAuthorizedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolEntryIntentRecordedV1 : ToolLifecycleRecordV1 { internal ToolEntryIntentRecordedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolExternalBoundaryEnteredV1 : ToolLifecycleRecordV1 { internal ToolExternalBoundaryEnteredV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolEffectEvidenceAdmittedV1 : ToolLifecycleRecordV1 { internal ToolEffectEvidenceAdmittedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolResultFinalizedV1 : ToolLifecycleRecordV1 { internal ToolResultFinalizedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolResultProjectedV1 : ToolLifecycleRecordV1 { internal ToolResultProjectedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolContinuationAuthorizedV1 : ToolLifecycleRecordV1 { internal ToolContinuationAuthorizedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolOrchestrationTerminalizedV1 : ToolLifecycleRecordV1 { internal ToolOrchestrationTerminalizedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }

internal static class ToolLifecycleAuthorityRecordCodecsV1
{
    internal static byte[] Encode(ToolLifecycleRecordV1 value){ArgumentNullException.ThrowIfNull(value);var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(4);w.WriteUInt64(1);WriteOperation(w,value.OperationId);w.WriteUInt64(2);w.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(value.SourcePosition));w.WriteUInt64(3);w.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.Authority));w.WriteUInt64(4);w.WriteUInt64(value.Disposition);w.WriteEndMap();return w.Encode();}

    internal static bool TryDecodeControl(ReadOnlyMemory<byte>b,out ToolControlRecordedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeArguments(ReadOnlyMemory<byte>b,out ToolArgumentsFinalizedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeApproval(ReadOnlyMemory<byte>b,out ToolApprovalDecidedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeDisposition(ReadOnlyMemory<byte>b,out ToolDispositionChosenV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeOwner(ReadOnlyMemory<byte>b,out ToolOwnerClaimedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeDispatch(ReadOnlyMemory<byte>b,out ToolDispatchAuthorizedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeEntry(ReadOnlyMemory<byte>b,out ToolEntryIntentRecordedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeBoundary(ReadOnlyMemory<byte>b,out ToolExternalBoundaryEnteredV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeEvidence(ReadOnlyMemory<byte>b,out ToolEffectEvidenceAdmittedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeFinal(ReadOnlyMemory<byte>b,out ToolResultFinalizedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeProjected(ReadOnlyMemory<byte>b,out ToolResultProjectedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeContinuation(ReadOnlyMemory<byte>b,out ToolContinuationAuthorizedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeTerminal(ReadOnlyMemory<byte>b,out ToolOrchestrationTerminalizedV1? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);

    internal static Hash256 ComputeHash(ToolControlRecordedV1 v)=>Hash("hpd.tool-control-recorded.v1",v);
    internal static Hash256 ComputeHash(ToolArgumentsFinalizedV1 v)=>Hash("hpd.tool-arguments-finalized.v1",v);
    internal static Hash256 ComputeHash(ToolApprovalDecidedV1 v)=>Hash("hpd.tool-approval-decided.v1",v);
    internal static Hash256 ComputeHash(ToolDispositionChosenV1 v)=>Hash("hpd.tool-disposition-chosen.v1",v);
    internal static Hash256 ComputeHash(ToolOwnerClaimedV1 v)=>Hash("hpd.tool-owner-claimed.v1",v);
    internal static Hash256 ComputeHash(ToolDispatchAuthorizedV1 v)=>Hash("hpd.tool-dispatch-authorized.v1",v);
    internal static Hash256 ComputeHash(ToolEntryIntentRecordedV1 v)=>Hash("hpd.tool-entry-intent-recorded.v1",v);
    internal static Hash256 ComputeHash(ToolExternalBoundaryEnteredV1 v)=>Hash("hpd.tool-external-boundary-entered.v1",v);
    internal static Hash256 ComputeHash(ToolEffectEvidenceAdmittedV1 v)=>Hash("hpd.tool-effect-evidence-admitted.v1",v);
    internal static Hash256 ComputeHash(ToolResultFinalizedV1 v)=>Hash("hpd.tool-result-finalized.v1",v);
    internal static Hash256 ComputeHash(ToolResultProjectedV1 v)=>Hash("hpd.tool-result-projected.v1",v);
    internal static Hash256 ComputeHash(ToolContinuationAuthorizedV1 v)=>Hash("hpd.tool-continuation-authorized.v1",v);
    internal static Hash256 ComputeHash(ToolOrchestrationTerminalizedV1 v)=>Hash("hpd.tool-orchestration-terminalized.v1",v);

    private static bool Decode<T>(ReadOnlyMemory<byte>b,Func<OperationId,JournalPositionV1,ExpectedAuthorityVectorV1,ushort,T> create,out T? value)where T:ToolLifecycleRecordV1{value=null;if(b.Length is 0 or>16384)return false;try{var r=new CborReader(b,CborConformanceMode.Ctap2Canonical,false);if(r.ReadStartMap()!=4||r.ReadUInt64()!=1)return false;var o=ReadOperation(r);if(r.ReadUInt64()!=2)return false;var p=AuthorityPositionCodecsV1.ReadJournal(r);if(r.ReadUInt64()!=3||!AuthorityVectorCodecsV1.TryDecodeVector(r.ReadEncodedValue(),out var a))return false;if(r.ReadUInt64()!=4)return false;var d=r.ReadUInt64();r.ReadEndMap();if(r.BytesRemaining!=0||d is 0 or>ushort.MaxValue)return false;var candidate=create(o,p,a!,(ushort)d);if(!Encode(candidate).AsSpan().SequenceEqual(b.Span))return false;value=candidate;return true;}catch(Exception e)when(e is CborContentException or InvalidOperationException or ArgumentException or OverflowException){return false;}}
    private static void WriteOperation(CborWriter w,OperationId v){Span<byte>b=stackalloc byte[16];if(!v.TryWriteBytes(b))throw new ArgumentException("An operation is required.");w.WriteByteString(b);}
    private static OperationId ReadOperation(CborReader r){Span<byte>b=stackalloc byte[16];if(!r.TryReadByteString(b,out var n)||n!=16)throw new CborContentException("An operation identifier is exactly 16 bytes.");return OperationId.FromValue(StableId128.FromBytes(b));}
    private static Hash256 Hash(string schema,ToolLifecycleRecordV1 value)=>AuthorityIntegrityHashV1.Compute(schema,1,0,Encode(value));
}
