using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class CrossOwnerLifecycleAuthorityRecordCodecsTests
{
    [Fact]
    public void All_twelve_cross_owner_lifecycle_records_round_trip_and_hash_independently()
    {
        var p=new JournalPositionV1(Session(),9);var a=ExpectedAuthorityVectorV1.Create(p.Session,[new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.Create())]);var o=OperationId.Create();
        var custodian=new CustodianReceiptAdmitted(o,p,a,1);var delivery=new DeliveryOrExportAdmitted(o,p,a,2);var fence=new PrivacyFenceCommitted(o,p,a,3);var terminal=new PrivacyTerminalFolded(o,p,a,4);
        var manifest=new ProjectionManifestProduced(o,p,a,5);var range=new ProjectionRangeTransportAccepted(o,p,a,6);var revision=new ProjectionRevisionSuperseded(o,p,a,7);var snapshot=new ProjectionSnapshotPinned(o,p,a,8);
        var receipt=new SemanticReceiptAdmitted(o,p,a,9);var source=new SourceFactCommitted(o,p,a,10);var subscriber=new SubscriberApplicationClaimed(o,p,a,11);var tombstone=new TombstoneCommittedBeforeRetirement(o,p,a,12);
        RoundTrip(custodian,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeCustodian);RoundTrip(delivery,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeDelivery);RoundTrip(fence,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeFence);RoundTrip(terminal,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodePrivacyTerminal);
        RoundTrip(manifest,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeManifest);RoundTrip(range,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeRange);RoundTrip(revision,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeRevision);RoundTrip(snapshot,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeSnapshot);
        RoundTrip(receipt,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeSemanticReceipt);RoundTrip(source,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeSource);RoundTrip(subscriber,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeSubscriber);RoundTrip(tombstone,CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeTombstone);
        Hash256[] hashes=[CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(custodian),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(delivery),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(fence),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(terminal),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(manifest),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(range),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(revision),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(snapshot),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(receipt),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(source),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(subscriber),CrossOwnerLifecycleAuthorityRecordCodecs.ComputeHash(tombstone)];Assert.Equal(12,hashes.Distinct().Count());
    }
    [Fact]
    public void Cross_owner_lifecycle_decoder_rejects_trailing_and_malformed_bytes()
    {var p=new JournalPositionV1(Session(),1);var a=ExpectedAuthorityVectorV1.Create(p.Session,[new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.Create())]);var bytes=CrossOwnerLifecycleAuthorityRecordCodecs.Encode(new PrivacyFenceCommitted(OperationId.Create(),p,a,1));Assert.False(CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeFence(bytes.Concat(new byte[]{0}).ToArray(),out _));Assert.False(CrossOwnerLifecycleAuthorityRecordCodecs.TryDecodeFence(new byte[]{0xff},out _));}
    private static void RoundTrip<T>(T value,Decoder<T> decode)where T:CrossOwnerLifecycleRecord{var bytes=CrossOwnerLifecycleAuthorityRecordCodecs.Encode(value);Assert.True(decode(bytes,out var result));Assert.Equal(value,result);}private delegate bool Decoder<T>(ReadOnlyMemory<byte>b,out T? v)where T:CrossOwnerLifecycleRecord;
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.Create(),LiveSessionId.Create());
}
