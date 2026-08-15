using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TransportAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Transport_payloads_round_trip_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority(); byte[] source = [1, 2, 3, 4];
        var command = new TransportAdapterCommandV1(session, authority, source); var receipt = new TransportAdapterReceiptV1(session, authority, source); source[0] = 99;
        var commandBytes = TransportAuthorityPayloadCodecV1.Encode(command); var receiptBytes = TransportAuthorityPayloadCodecV1.Encode(receipt);
        Assert.True(TransportAuthorityPayloadCodecV1.TryDecodeTransportAdapterCommand(commandBytes, out var decodedCommand));
        Assert.True(TransportAuthorityPayloadCodecV1.TryDecodeTransportAdapterReceipt(receiptBytes, out var decodedReceipt));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCommand!.Body);
        Assert.Equal(commandBytes, TransportAuthorityPayloadCodecV1.Encode(decodedCommand));
        Assert.Equal(receiptBytes, TransportAuthorityPayloadCodecV1.Encode(decodedReceipt!));
        Assert.NotEqual(TransportAuthorityPayloadCodecV1.ComputeHash(command), TransportAuthorityPayloadCodecV1.ComputeHash(receipt));
    }

    [Fact]
    public void Transport_payloads_reject_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new TransportAdapterCommandV1(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new TransportAdapterReceiptV1(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Transport_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority(); var canonical = TransportAuthorityPayloadCodecV1.Encode(new TransportAdapterCommandV1(session, authority, [7]));
        Assert.False(TransportAuthorityPayloadCodecV1.TryDecodeTransportAdapterCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(TransportAuthorityPayloadCodecV1.TryDecodeTransportAdapterReceipt(reordered.Encode(), out _));
        Assert.False(TransportAuthorityPayloadCodecV1.TryDecodeTransportAdapterReceipt(new byte[66_561], out _));
    }

    [Fact]
    public void Transport_registrations_join_discriminators_owner_and_session()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var command = TransportAuthorityPayloadCodecV1.Encode(new TransportAdapterCommandV1(session, authority, []));
        var receipt = TransportAuthorityPayloadCodecV1.Encode(new TransportAdapterReceiptV1(session, authority, []));
        Assert.Equal((ushort)31, TransportAuthorityPayloadRegistrationsV1.TransportAdapterCommandDiscriminator);
        Assert.Equal((ushort)32, TransportAuthorityPayloadRegistrationsV1.TransportAdapterReceiptDiscriminator);
        Assert.Equal(OwnerSliceId.S11, TransportAuthorityPayloadRegistrationsV1.TransportAdapterCommand.Owner);
        Assert.Equal(OwnerSliceId.S11, TransportAuthorityPayloadRegistrationsV1.TransportAdapterReceipt.Owner);
        Assert.True(TransportAuthorityPayloadRegistrationsV1.TransportAdapterCommand.Validate(command, session));
        Assert.True(TransportAuthorityPayloadRegistrationsV1.TransportAdapterReceipt.Validate(receipt, session));
        Assert.False(TransportAuthorityPayloadRegistrationsV1.TransportAdapterReceipt.Validate(receipt, other));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2)); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }
    private static T Id<T>(byte value) where T : struct
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes); return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) : typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException(); }
}
