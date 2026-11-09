using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class TransportAuthorityPayloadCodecTests
{
    [Fact]
    public void Transport_payloads_round_trip_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority(); byte[] source = [1, 2, 3, 4];
        var command = new TransportAdapterCommand(session, authority, source); var receipt = new TransportAdapterReceipt(session, authority, source); source[0] = 99;
        var commandBytes = TransportAuthorityPayloadCodec.Encode(command); var receiptBytes = TransportAuthorityPayloadCodec.Encode(receipt);
        Assert.True(TransportAuthorityPayloadCodec.TryDecodeTransportAdapterCommand(commandBytes, out var decodedCommand));
        Assert.True(TransportAuthorityPayloadCodec.TryDecodeTransportAdapterReceipt(receiptBytes, out var decodedReceipt));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCommand!.Body);
        Assert.Equal(commandBytes, TransportAuthorityPayloadCodec.Encode(decodedCommand));
        Assert.Equal(receiptBytes, TransportAuthorityPayloadCodec.Encode(decodedReceipt!));
        Assert.NotEqual(TransportAuthorityPayloadCodec.ComputeHash(command), TransportAuthorityPayloadCodec.ComputeHash(receipt));
    }

    [Fact]
    public void Transport_payloads_reject_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new TransportAdapterCommand(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new TransportAdapterReceipt(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Transport_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority(); var canonical = TransportAuthorityPayloadCodec.Encode(new TransportAdapterCommand(session, authority, [7]));
        Assert.False(TransportAuthorityPayloadCodec.TryDecodeTransportAdapterCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(TransportAuthorityPayloadCodec.TryDecodeTransportAdapterReceipt(reordered.Encode(), out _));
        Assert.False(TransportAuthorityPayloadCodec.TryDecodeTransportAdapterReceipt(new byte[66_561], out _));
    }

    [Fact]
    public void Transport_registrations_join_discriminators_owner_and_session()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var command = TransportAuthorityPayloadCodec.Encode(new TransportAdapterCommand(session, authority, []));
        var receipt = TransportAuthorityPayloadCodec.Encode(new TransportAdapterReceipt(session, authority, []));
        Assert.Equal((ushort)31, TransportAuthorityPayloadRegistrations.TransportAdapterCommandDiscriminator);
        Assert.Equal((ushort)32, TransportAuthorityPayloadRegistrations.TransportAdapterReceiptDiscriminator);
        Assert.Equal(OwnerSliceId.S11, TransportAuthorityPayloadRegistrations.TransportAdapterCommand.Owner);
        Assert.Equal(OwnerSliceId.S11, TransportAuthorityPayloadRegistrations.TransportAdapterReceipt.Owner);
        Assert.True(TransportAuthorityPayloadRegistrations.TransportAdapterCommand.Validate(command, session));
        Assert.True(TransportAuthorityPayloadRegistrations.TransportAdapterReceipt.Validate(receipt, session));
        Assert.False(TransportAuthorityPayloadRegistrations.TransportAdapterReceipt.Validate(receipt, other));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2)); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }
    private static T Id<T>(byte value) where T : struct
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes); return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) : typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException(); }
}
