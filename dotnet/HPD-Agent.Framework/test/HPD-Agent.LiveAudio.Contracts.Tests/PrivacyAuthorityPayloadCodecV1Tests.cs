using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class PrivacyAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Registered_privacy_payloads_round_trip_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var copy = new CopyReservationCommandV1(session, authority, source);
        var deletion = new PrivacyDeleteEffectV1(session, authority, source);
        var receipt = new PrivacyCustodianReceiptV1(session, authority, source);
        source[0] = 99;
        var copyBytes = PrivacyAuthorityPayloadCodecV1.Encode(copy);
        var deletionBytes = PrivacyAuthorityPayloadCodecV1.Encode(deletion);
        var receiptBytes = PrivacyAuthorityPayloadCodecV1.Encode(receipt);
        Assert.True(PrivacyAuthorityPayloadCodecV1.TryDecodeCopyReservationCommand(copyBytes, out var decodedCopy));
        Assert.True(PrivacyAuthorityPayloadCodecV1.TryDecodePrivacyDeleteEffect(deletionBytes, out var decodedDeletion));
        Assert.True(PrivacyAuthorityPayloadCodecV1.TryDecodePrivacyCustodianReceipt(receiptBytes, out var decodedReceipt));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCopy!.Body);
        Assert.Equal(copyBytes, PrivacyAuthorityPayloadCodecV1.Encode(decodedCopy));
        Assert.Equal(deletionBytes, PrivacyAuthorityPayloadCodecV1.Encode(decodedDeletion!));
        Assert.Equal(receiptBytes, PrivacyAuthorityPayloadCodecV1.Encode(decodedReceipt!));
        Assert.Equal(3, new[] { PrivacyAuthorityPayloadCodecV1.ComputeHash(copy), PrivacyAuthorityPayloadCodecV1.ComputeHash(deletion), PrivacyAuthorityPayloadCodecV1.ComputeHash(receipt) }.Distinct().Count());
    }

    [Fact]
    public void Privacy_payloads_reject_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new CopyReservationCommandV1(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new PrivacyDeleteEffectV1(session, authority, new byte[65_537]));
        Assert.Throws<ArgumentException>(() => new PrivacyCustodianReceiptV1(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Privacy_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = PrivacyAuthorityPayloadCodecV1.Encode(new CopyReservationCommandV1(session, authority, [7]));
        Assert.False(PrivacyAuthorityPayloadCodecV1.TryDecodeCopyReservationCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(PrivacyAuthorityPayloadCodecV1.TryDecodePrivacyDeleteEffect(reordered.Encode(), out _));
        Assert.False(PrivacyAuthorityPayloadCodecV1.TryDecodePrivacyCustodianReceipt(new byte[66_561], out _));
    }

    [Fact]
    public void Privacy_registrations_join_discriminators_owner_and_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var payloads = new[]
        {
            PrivacyAuthorityPayloadCodecV1.Encode(new CopyReservationCommandV1(session, authority, [])),
            PrivacyAuthorityPayloadCodecV1.Encode(new PrivacyDeleteEffectV1(session, authority, [])),
            PrivacyAuthorityPayloadCodecV1.Encode(new PrivacyCustodianReceiptV1(session, authority, [])),
        };
        var registrations = new[] { PrivacyAuthorityPayloadRegistrationsV1.CopyReservationCommand, PrivacyAuthorityPayloadRegistrationsV1.PrivacyDeleteEffect, PrivacyAuthorityPayloadRegistrationsV1.PrivacyCustodianReceipt };
        Assert.Equal(new ushort[] { 26, 27, 28 }, new[] { PrivacyAuthorityPayloadRegistrationsV1.CopyReservationCommandDiscriminator, PrivacyAuthorityPayloadRegistrationsV1.PrivacyDeleteEffectDiscriminator, PrivacyAuthorityPayloadRegistrationsV1.PrivacyCustodianReceiptDiscriminator });
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
