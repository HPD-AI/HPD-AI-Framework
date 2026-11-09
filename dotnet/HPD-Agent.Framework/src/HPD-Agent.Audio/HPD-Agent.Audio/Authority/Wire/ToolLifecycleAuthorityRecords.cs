using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract record ToolLifecycleRecord
{
    protected ToolLifecycleRecord(OperationId operationId, JournalPositionV1 sourcePosition, ExpectedAuthorityVectorV1 authority, ushort disposition)
    { if(!operationId.IsValid||!sourcePosition.IsValid||authority is null||authority.Session!=sourcePosition.Session||disposition==0) throw new ArgumentException("Invalid tool lifecycle record."); OperationId=operationId;SourcePosition=sourcePosition;Authority=authority;Disposition=disposition; }
    internal OperationId OperationId {get;} internal JournalPositionV1 SourcePosition {get;} internal ExpectedAuthorityVectorV1 Authority {get;} internal ushort Disposition {get;}
}

internal sealed record ToolControlRecorded : ToolLifecycleRecord { internal ToolControlRecorded(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolArgumentsFinalized : ToolLifecycleRecord { internal ToolArgumentsFinalized(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolApprovalDecided : ToolLifecycleRecord { internal ToolApprovalDecided(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolDispositionChosen : ToolLifecycleRecord { internal ToolDispositionChosen(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolOwnerClaimed : ToolLifecycleRecord { internal ToolOwnerClaimed(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolDispatchAuthorized : ToolLifecycleRecord { internal ToolDispatchAuthorized(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolEntryIntentRecorded : ToolLifecycleRecord { internal ToolEntryIntentRecorded(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolExternalBoundaryEntered : ToolLifecycleRecord { internal ToolExternalBoundaryEntered(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolEffectEvidenceAdmitted : ToolLifecycleRecord { internal ToolEffectEvidenceAdmitted(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolResultFinalized : ToolLifecycleRecord { internal ToolResultFinalized(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolResultProjected : ToolLifecycleRecord { internal ToolResultProjected(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolContinuationAuthorized : ToolLifecycleRecord { internal ToolContinuationAuthorized(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ToolOrchestrationTerminalized : ToolLifecycleRecord { internal ToolOrchestrationTerminalized(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }

internal static class ToolLifecycleAuthorityRecordCodecs
{
    internal static byte[] Encode(ToolLifecycleRecord value){ArgumentNullException.ThrowIfNull(value);var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(4);w.WriteUInt64(1);WriteOperation(w,value.OperationId);w.WriteUInt64(2);w.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(value.SourcePosition));w.WriteUInt64(3);w.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.Authority));w.WriteUInt64(4);w.WriteUInt64(value.Disposition);w.WriteEndMap();return w.Encode();}

    internal static bool TryDecodeControl(ReadOnlyMemory<byte>b,out ToolControlRecorded? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeArguments(ReadOnlyMemory<byte>b,out ToolArgumentsFinalized? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeApproval(ReadOnlyMemory<byte>b,out ToolApprovalDecided? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeDisposition(ReadOnlyMemory<byte>b,out ToolDispositionChosen? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeOwner(ReadOnlyMemory<byte>b,out ToolOwnerClaimed? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeDispatch(ReadOnlyMemory<byte>b,out ToolDispatchAuthorized? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeEntry(ReadOnlyMemory<byte>b,out ToolEntryIntentRecorded? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeBoundary(ReadOnlyMemory<byte>b,out ToolExternalBoundaryEntered? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeEvidence(ReadOnlyMemory<byte>b,out ToolEffectEvidenceAdmitted? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeFinal(ReadOnlyMemory<byte>b,out ToolResultFinalized? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeProjected(ReadOnlyMemory<byte>b,out ToolResultProjected? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeContinuation(ReadOnlyMemory<byte>b,out ToolContinuationAuthorized? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);
    internal static bool TryDecodeTerminal(ReadOnlyMemory<byte>b,out ToolOrchestrationTerminalized? v)=>Decode(b,static(o,p,a,d)=>new(o,p,a,d),out v);

    internal static Hash256 ComputeHash(ToolControlRecorded v)=>Hash("hpd.tool-control-recorded.v1",v);
    internal static Hash256 ComputeHash(ToolArgumentsFinalized v)=>Hash("hpd.tool-arguments-finalized.v1",v);
    internal static Hash256 ComputeHash(ToolApprovalDecided v)=>Hash("hpd.tool-approval-decided.v1",v);
    internal static Hash256 ComputeHash(ToolDispositionChosen v)=>Hash("hpd.tool-disposition-chosen.v1",v);
    internal static Hash256 ComputeHash(ToolOwnerClaimed v)=>Hash("hpd.tool-owner-claimed.v1",v);
    internal static Hash256 ComputeHash(ToolDispatchAuthorized v)=>Hash("hpd.tool-dispatch-authorized.v1",v);
    internal static Hash256 ComputeHash(ToolEntryIntentRecorded v)=>Hash("hpd.tool-entry-intent-recorded.v1",v);
    internal static Hash256 ComputeHash(ToolExternalBoundaryEntered v)=>Hash("hpd.tool-external-boundary-entered.v1",v);
    internal static Hash256 ComputeHash(ToolEffectEvidenceAdmitted v)=>Hash("hpd.tool-effect-evidence-admitted.v1",v);
    internal static Hash256 ComputeHash(ToolResultFinalized v)=>Hash("hpd.tool-result-finalized.v1",v);
    internal static Hash256 ComputeHash(ToolResultProjected v)=>Hash("hpd.tool-result-projected.v1",v);
    internal static Hash256 ComputeHash(ToolContinuationAuthorized v)=>Hash("hpd.tool-continuation-authorized.v1",v);
    internal static Hash256 ComputeHash(ToolOrchestrationTerminalized v)=>Hash("hpd.tool-orchestration-terminalized.v1",v);

    private static bool Decode<T>(ReadOnlyMemory<byte>b,Func<OperationId,JournalPositionV1,ExpectedAuthorityVectorV1,ushort,T> create,out T? value)where T:ToolLifecycleRecord{value=null;if(b.Length is 0 or>16384)return false;try{var r=new CborReader(b,CborConformanceMode.Ctap2Canonical,false);if(r.ReadStartMap()!=4||r.ReadUInt64()!=1)return false;var o=ReadOperation(r);if(r.ReadUInt64()!=2)return false;var p=AuthorityPositionCodecsV1.ReadJournal(r);if(r.ReadUInt64()!=3||!AuthorityVectorCodecsV1.TryDecodeVector(r.ReadEncodedValue(),out var a))return false;if(r.ReadUInt64()!=4)return false;var d=r.ReadUInt64();r.ReadEndMap();if(r.BytesRemaining!=0||d is 0 or>ushort.MaxValue)return false;var candidate=create(o,p,a!,(ushort)d);if(!Encode(candidate).AsSpan().SequenceEqual(b.Span))return false;value=candidate;return true;}catch(Exception e)when(e is CborContentException or InvalidOperationException or ArgumentException or OverflowException){return false;}}
    private static void WriteOperation(CborWriter w,OperationId v){Span<byte>b=stackalloc byte[16];if(!v.TryWriteBytes(b))throw new ArgumentException("An operation is required.");w.WriteByteString(b);}
    private static OperationId ReadOperation(CborReader r){Span<byte>b=stackalloc byte[16];if(!r.TryReadByteString(b,out var n)||n!=16)throw new CborContentException("An operation identifier is exactly 16 bytes.");return OperationId.FromValue(StableId128.FromBytes(b));}
    private static Hash256 Hash(string schema,ToolLifecycleRecord value)=>AuthorityIntegrityHashV1.Compute(schema,1,0,Encode(value));
}
