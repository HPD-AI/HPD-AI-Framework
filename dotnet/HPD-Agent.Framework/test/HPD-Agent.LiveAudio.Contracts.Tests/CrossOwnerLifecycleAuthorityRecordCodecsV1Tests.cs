using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class CrossOwnerLifecycleAuthorityRecordCodecsV1Tests
{
    [Fact]
    public void All_twelve_cross_owner_lifecycle_records_round_trip_and_hash_independently()
    {
        var p=new JournalPositionV1(Session(),9);var a=ExpectedAuthorityVectorV1.Create(p.Session,[new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.Create())]);var o=OperationId.Create();
        var custodian=new CustodianReceiptAdmittedV1(o,p,a,1);var delivery=new DeliveryOrExportAdmittedV1(o,p,a,2);var fence=new PrivacyFenceCommittedV1(o,p,a,3);var terminal=new PrivacyTerminalFoldedV1(o,p,a,4);
        var manifest=new ProjectionManifestProducedV1(o,p,a,5);var range=new ProjectionRangeTransportAcceptedV1(o,p,a,6);var revision=new ProjectionRevisionSupersededV1(o,p,a,7);var snapshot=new ProjectionSnapshotPinnedV1(o,p,a,8);
        var receipt=new SemanticReceiptAdmittedV1(o,p,a,9);var source=new SourceFactCommittedV1(o,p,a,10);var subscriber=new SubscriberApplicationClaimedV1(o,p,a,11);var tombstone=new TombstoneCommittedBeforeRetirementV1(o,p,a,12);
        RoundTrip(custodian,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeCustodian);RoundTrip(delivery,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeDelivery);RoundTrip(fence,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeFence);RoundTrip(terminal,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodePrivacyTerminal);
        RoundTrip(manifest,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeManifest);RoundTrip(range,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeRange);RoundTrip(revision,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeRevision);RoundTrip(snapshot,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeSnapshot);
        RoundTrip(receipt,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeSemanticReceipt);RoundTrip(source,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeSource);RoundTrip(subscriber,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeSubscriber);RoundTrip(tombstone,CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeTombstone);
        Hash256[] hashes=[CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(custodian),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(delivery),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(fence),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(terminal),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(manifest),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(range),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(revision),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(snapshot),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(receipt),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(source),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(subscriber),CrossOwnerLifecycleAuthorityRecordCodecsV1.ComputeHash(tombstone)];Assert.Equal(12,hashes.Distinct().Count());
    }
    [Fact]
    public void Cross_owner_lifecycle_decoder_rejects_trailing_and_malformed_bytes()
    {var p=new JournalPositionV1(Session(),1);var a=ExpectedAuthorityVectorV1.Create(p.Session,[new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.Create())]);var bytes=CrossOwnerLifecycleAuthorityRecordCodecsV1.Encode(new PrivacyFenceCommittedV1(OperationId.Create(),p,a,1));Assert.False(CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeFence(bytes.Concat(new byte[]{0}).ToArray(),out _));Assert.False(CrossOwnerLifecycleAuthorityRecordCodecsV1.TryDecodeFence(new byte[]{0xff},out _));}
    private static void RoundTrip<T>(T value,Decoder<T> decode)where T:CrossOwnerLifecycleRecordV1{var bytes=CrossOwnerLifecycleAuthorityRecordCodecsV1.Encode(value);Assert.True(decode(bytes,out var result));Assert.Equal(value,result);}private delegate bool Decoder<T>(ReadOnlyMemory<byte>b,out T? v)where T:CrossOwnerLifecycleRecordV1;
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.Create(),LiveSessionId.Create());
}
