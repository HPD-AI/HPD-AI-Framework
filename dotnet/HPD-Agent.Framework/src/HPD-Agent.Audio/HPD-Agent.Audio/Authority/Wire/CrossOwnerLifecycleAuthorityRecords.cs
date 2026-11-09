using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract record CrossOwnerLifecycleRecord
{
    protected CrossOwnerLifecycleRecord(OperationId operationId,JournalPositionV1 sourcePosition,ExpectedAuthorityVectorV1 authority,ushort disposition)
    {AuthorityLifecycleRecordCodecV1.Validate(operationId,sourcePosition,authority,disposition);OperationId=operationId;SourcePosition=sourcePosition;Authority=authority;Disposition=disposition;}
    internal OperationId OperationId{get;} internal JournalPositionV1 SourcePosition{get;} internal ExpectedAuthorityVectorV1 Authority{get;} internal ushort Disposition{get;}
}

internal sealed record CustodianReceiptAdmitted : CrossOwnerLifecycleRecord { internal CustodianReceiptAdmitted(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record DeliveryOrExportAdmitted : CrossOwnerLifecycleRecord { internal DeliveryOrExportAdmitted(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record PrivacyFenceCommitted : CrossOwnerLifecycleRecord { internal PrivacyFenceCommitted(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record PrivacyTerminalFolded : CrossOwnerLifecycleRecord { internal PrivacyTerminalFolded(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ProjectionManifestProduced : CrossOwnerLifecycleRecord { internal ProjectionManifestProduced(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ProjectionRangeTransportAccepted : CrossOwnerLifecycleRecord { internal ProjectionRangeTransportAccepted(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ProjectionRevisionSuperseded : CrossOwnerLifecycleRecord { internal ProjectionRevisionSuperseded(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ProjectionSnapshotPinned : CrossOwnerLifecycleRecord { internal ProjectionSnapshotPinned(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record SemanticReceiptAdmitted : CrossOwnerLifecycleRecord { internal SemanticReceiptAdmitted(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record SourceFactCommitted : CrossOwnerLifecycleRecord { internal SourceFactCommitted(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record SubscriberApplicationClaimed : CrossOwnerLifecycleRecord { internal SubscriberApplicationClaimed(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record TombstoneCommittedBeforeRetirement : CrossOwnerLifecycleRecord { internal TombstoneCommittedBeforeRetirement(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }

internal static class CrossOwnerLifecycleAuthorityRecordCodecs
{
    internal static byte[] Encode(CrossOwnerLifecycleRecord v)=>AuthorityLifecycleRecordCodecV1.Encode(v.OperationId,v.SourcePosition,v.Authority,v.Disposition);
    internal static bool TryDecodeCustodian(ReadOnlyMemory<byte>b,out CustodianReceiptAdmitted? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeDelivery(ReadOnlyMemory<byte>b,out DeliveryOrExportAdmitted? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeFence(ReadOnlyMemory<byte>b,out PrivacyFenceCommitted? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodePrivacyTerminal(ReadOnlyMemory<byte>b,out PrivacyTerminalFolded? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeManifest(ReadOnlyMemory<byte>b,out ProjectionManifestProduced? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeRange(ReadOnlyMemory<byte>b,out ProjectionRangeTransportAccepted? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeRevision(ReadOnlyMemory<byte>b,out ProjectionRevisionSuperseded? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeSnapshot(ReadOnlyMemory<byte>b,out ProjectionSnapshotPinned? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeSemanticReceipt(ReadOnlyMemory<byte>b,out SemanticReceiptAdmitted? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeSource(ReadOnlyMemory<byte>b,out SourceFactCommitted? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeSubscriber(ReadOnlyMemory<byte>b,out SubscriberApplicationClaimed? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeTombstone(ReadOnlyMemory<byte>b,out TombstoneCommittedBeforeRetirement? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static Hash256 ComputeHash(CustodianReceiptAdmitted v)=>Hash("hpd.custodian-receipt-admitted.v1",v);
    internal static Hash256 ComputeHash(DeliveryOrExportAdmitted v)=>Hash("hpd.delivery-or-export-admitted.v1",v);
    internal static Hash256 ComputeHash(PrivacyFenceCommitted v)=>Hash("hpd.privacy-fence-committed.v1",v);
    internal static Hash256 ComputeHash(PrivacyTerminalFolded v)=>Hash("hpd.privacy-terminal-folded.v1",v);
    internal static Hash256 ComputeHash(ProjectionManifestProduced v)=>Hash("hpd.projection-manifest-produced.v1",v);
    internal static Hash256 ComputeHash(ProjectionRangeTransportAccepted v)=>Hash("hpd.projection-range-transport-accepted.v1",v);
    internal static Hash256 ComputeHash(ProjectionRevisionSuperseded v)=>Hash("hpd.projection-revision-superseded.v1",v);
    internal static Hash256 ComputeHash(ProjectionSnapshotPinned v)=>Hash("hpd.projection-snapshot-pinned.v1",v);
    internal static Hash256 ComputeHash(SemanticReceiptAdmitted v)=>Hash("hpd.semantic-receipt-admitted.v1",v);
    internal static Hash256 ComputeHash(SourceFactCommitted v)=>Hash("hpd.source-fact-committed.v1",v);
    internal static Hash256 ComputeHash(SubscriberApplicationClaimed v)=>Hash("hpd.subscriber-application-claimed.v1",v);
    internal static Hash256 ComputeHash(TombstoneCommittedBeforeRetirement v)=>Hash("hpd.tombstone-committed-before-retirement.v1",v);
    private static bool Decode<T>(ReadOnlyMemory<byte>b,Func<DecodedAuthorityLifecycleRecordV1,T> create,out T? value)where T:CrossOwnerLifecycleRecord{value=null;if(!AuthorityLifecycleRecordCodecV1.TryDecode(b,out var decoded))return false;var candidate=create(decoded);if(!Encode(candidate).AsSpan().SequenceEqual(b.Span))return false;value=candidate;return true;}
    private static Hash256 Hash(string schema,CrossOwnerLifecycleRecord v)=>AuthorityIntegrityHashV1.Compute(schema,1,0,Encode(v));
}
