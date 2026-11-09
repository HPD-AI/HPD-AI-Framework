using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class PrivacyAuthorityPayloadCodecTests
{
    [Fact]
    public void Registered_privacy_payloads_round_trip_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var copy = new CopyReservationCommand(session, authority, source);
        var deletion = new PrivacyDeleteEffect(session, authority, source);
        var receipt = new PrivacyCustodianReceipt(session, authority, source);
        source[0] = 99;
        var copyBytes = PrivacyAuthorityPayloadCodec.Encode(copy);
        var deletionBytes = PrivacyAuthorityPayloadCodec.Encode(deletion);
        var receiptBytes = PrivacyAuthorityPayloadCodec.Encode(receipt);
        Assert.True(PrivacyAuthorityPayloadCodec.TryDecodeCopyReservationCommand(copyBytes, out var decodedCopy));
        Assert.True(PrivacyAuthorityPayloadCodec.TryDecodePrivacyDeleteEffect(deletionBytes, out var decodedDeletion));
        Assert.True(PrivacyAuthorityPayloadCodec.TryDecodePrivacyCustodianReceipt(receiptBytes, out var decodedReceipt));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCopy!.Body);
        Assert.Equal(copyBytes, PrivacyAuthorityPayloadCodec.Encode(decodedCopy));
        Assert.Equal(deletionBytes, PrivacyAuthorityPayloadCodec.Encode(decodedDeletion!));
        Assert.Equal(receiptBytes, PrivacyAuthorityPayloadCodec.Encode(decodedReceipt!));
        Assert.Equal(3, new[] { PrivacyAuthorityPayloadCodec.ComputeHash(copy), PrivacyAuthorityPayloadCodec.ComputeHash(deletion), PrivacyAuthorityPayloadCodec.ComputeHash(receipt) }.Distinct().Count());
    }

    [Fact]
    public void Privacy_payloads_reject_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new CopyReservationCommand(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new PrivacyDeleteEffect(session, authority, new byte[65_537]));
        Assert.Throws<ArgumentException>(() => new PrivacyCustodianReceipt(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Privacy_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = PrivacyAuthorityPayloadCodec.Encode(new CopyReservationCommand(session, authority, [7]));
        Assert.False(PrivacyAuthorityPayloadCodec.TryDecodeCopyReservationCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(PrivacyAuthorityPayloadCodec.TryDecodePrivacyDeleteEffect(reordered.Encode(), out _));
        Assert.False(PrivacyAuthorityPayloadCodec.TryDecodePrivacyCustodianReceipt(new byte[66_561], out _));
    }

    [Fact]
    public void Privacy_registrations_join_discriminators_owner_and_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var payloads = new[]
        {
            PrivacyAuthorityPayloadCodec.Encode(new CopyReservationCommand(session, authority, [])),
            PrivacyAuthorityPayloadCodec.Encode(new PrivacyDeleteEffect(session, authority, [])),
            PrivacyAuthorityPayloadCodec.Encode(new PrivacyCustodianReceipt(session, authority, [])),
        };
        var registrations = new[] { PrivacyAuthorityPayloadRegistrations.CopyReservationCommand, PrivacyAuthorityPayloadRegistrations.PrivacyDeleteEffect, PrivacyAuthorityPayloadRegistrations.PrivacyCustodianReceipt };
        Assert.Equal(new ushort[] { 26, 27, 28 }, new[] { PrivacyAuthorityPayloadRegistrations.CopyReservationCommandDiscriminator, PrivacyAuthorityPayloadRegistrations.PrivacyDeleteEffectDiscriminator, PrivacyAuthorityPayloadRegistrations.PrivacyCustodianReceiptDiscriminator });
        for (var index = 0; index < registrations.Length; index++)
        {
            Assert.Equal(OwnerSliceId.S9, registrations[index].Owner);
            Assert.True(registrations[index].Validate(payloads[index], session));
            Assert.False(registrations[index].Validate(payloads[index], other));
        }
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    {
        var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2));
        return (session, ExpectedAuthorityVectorV1.Create(session, []));
    }

    private static T Id<T>(byte value) where T : struct
    {
        Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes);
        return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) :
            typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException();
    }
}
