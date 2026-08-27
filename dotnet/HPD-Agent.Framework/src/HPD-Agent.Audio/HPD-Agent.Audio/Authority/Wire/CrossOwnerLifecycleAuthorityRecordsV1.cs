using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal abstract record CrossOwnerLifecycleRecordV1
{
    protected CrossOwnerLifecycleRecordV1(OperationId operationId,JournalPositionV1 sourcePosition,ExpectedAuthorityVectorV1 authority,ushort disposition)
    {AuthorityLifecycleRecordCodecV1.Validate(operationId,sourcePosition,authority,disposition);OperationId=operationId;SourcePosition=sourcePosition;Authority=authority;Disposition=disposition;}
    internal OperationId OperationId{get;} internal JournalPositionV1 SourcePosition{get;} internal ExpectedAuthorityVectorV1 Authority{get;} internal ushort Disposition{get;}
}

internal sealed record CustodianReceiptAdmittedV1 : CrossOwnerLifecycleRecordV1 { internal CustodianReceiptAdmittedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record DeliveryOrExportAdmittedV1 : CrossOwnerLifecycleRecordV1 { internal DeliveryOrExportAdmittedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record PrivacyFenceCommittedV1 : CrossOwnerLifecycleRecordV1 { internal PrivacyFenceCommittedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record PrivacyTerminalFoldedV1 : CrossOwnerLifecycleRecordV1 { internal PrivacyTerminalFoldedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ProjectionManifestProducedV1 : CrossOwnerLifecycleRecordV1 { internal ProjectionManifestProducedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ProjectionRangeTransportAcceptedV1 : CrossOwnerLifecycleRecordV1 { internal ProjectionRangeTransportAcceptedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ProjectionRevisionSupersededV1 : CrossOwnerLifecycleRecordV1 { internal ProjectionRevisionSupersededV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record ProjectionSnapshotPinnedV1 : CrossOwnerLifecycleRecordV1 { internal ProjectionSnapshotPinnedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record SemanticReceiptAdmittedV1 : CrossOwnerLifecycleRecordV1 { internal SemanticReceiptAdmittedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record SourceFactCommittedV1 : CrossOwnerLifecycleRecordV1 { internal SourceFactCommittedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record SubscriberApplicationClaimedV1 : CrossOwnerLifecycleRecordV1 { internal SubscriberApplicationClaimedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record TombstoneCommittedBeforeRetirementV1 : CrossOwnerLifecycleRecordV1 { internal TombstoneCommittedBeforeRetirementV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }

internal static class CrossOwnerLifecycleAuthorityRecordCodecsV1
{
    internal static byte[] Encode(CrossOwnerLifecycleRecordV1 v)=>AuthorityLifecycleRecordCodecV1.Encode(v.OperationId,v.SourcePosition,v.Authority,v.Disposition);
    internal static bool TryDecodeCustodian(ReadOnlyMemory<byte>b,out CustodianReceiptAdmittedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeDelivery(ReadOnlyMemory<byte>b,out DeliveryOrExportAdmittedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeFence(ReadOnlyMemory<byte>b,out PrivacyFenceCommittedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodePrivacyTerminal(ReadOnlyMemory<byte>b,out PrivacyTerminalFoldedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeManifest(ReadOnlyMemory<byte>b,out ProjectionManifestProducedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeRange(ReadOnlyMemory<byte>b,out ProjectionRangeTransportAcceptedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeRevision(ReadOnlyMemory<byte>b,out ProjectionRevisionSupersededV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeSnapshot(ReadOnlyMemory<byte>b,out ProjectionSnapshotPinnedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeSemanticReceipt(ReadOnlyMemory<byte>b,out SemanticReceiptAdmittedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeSource(ReadOnlyMemory<byte>b,out SourceFactCommittedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeSubscriber(ReadOnlyMemory<byte>b,out SubscriberApplicationClaimedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeTombstone(ReadOnlyMemory<byte>b,out TombstoneCommittedBeforeRetirementV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static Hash256 ComputeHash(CustodianReceiptAdmittedV1 v)=>Hash("hpd.custodian-receipt-admitted.v1",v);
    internal static Hash256 ComputeHash(DeliveryOrExportAdmittedV1 v)=>Hash("hpd.delivery-or-export-admitted.v1",v);
    internal static Hash256 ComputeHash(PrivacyFenceCommittedV1 v)=>Hash("hpd.privacy-fence-committed.v1",v);
    internal static Hash256 ComputeHash(PrivacyTerminalFoldedV1 v)=>Hash("hpd.privacy-terminal-folded.v1",v);
    internal static Hash256 ComputeHash(ProjectionManifestProducedV1 v)=>Hash("hpd.projection-manifest-produced.v1",v);
    internal static Hash256 ComputeHash(ProjectionRangeTransportAcceptedV1 v)=>Hash("hpd.projection-range-transport-accepted.v1",v);
    internal static Hash256 ComputeHash(ProjectionRevisionSupersededV1 v)=>Hash("hpd.projection-revision-superseded.v1",v);
    internal static Hash256 ComputeHash(ProjectionSnapshotPinnedV1 v)=>Hash("hpd.projection-snapshot-pinned.v1",v);
    internal static Hash256 ComputeHash(SemanticReceiptAdmittedV1 v)=>Hash("hpd.semantic-receipt-admitted.v1",v);
    internal static Hash256 ComputeHash(SourceFactCommittedV1 v)=>Hash("hpd.source-fact-committed.v1",v);
    internal static Hash256 ComputeHash(SubscriberApplicationClaimedV1 v)=>Hash("hpd.subscriber-application-claimed.v1",v);
    internal static Hash256 ComputeHash(TombstoneCommittedBeforeRetirementV1 v)=>Hash("hpd.tombstone-committed-before-retirement.v1",v);
    private static bool Decode<T>(ReadOnlyMemory<byte>b,Func<DecodedAuthorityLifecycleRecordV1,T> create,out T? value)where T:CrossOwnerLifecycleRecordV1{value=null;if(!AuthorityLifecycleRecordCodecV1.TryDecode(b,out var decoded))return false;var candidate=create(decoded);if(!Encode(candidate).AsSpan().SequenceEqual(b.Span))return false;value=candidate;return true;}
    private static Hash256 Hash(string schema,CrossOwnerLifecycleRecordV1 v)=>AuthorityIntegrityHashV1.Compute(schema,1,0,Encode(v));
}
