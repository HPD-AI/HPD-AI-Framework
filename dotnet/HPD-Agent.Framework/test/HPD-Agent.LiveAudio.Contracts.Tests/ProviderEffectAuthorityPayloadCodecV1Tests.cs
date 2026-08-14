using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ProviderEffectAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Registered_provider_effect_payloads_round_trip_canonically_and_own_body_bytes()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var command = new ProviderEffectCommandV1(session, authority, source);
        var receipt = new ProviderEffectReceiptV1(session, authority, source);
        source[0] = 99;

        var commandBytes = ProviderEffectAuthorityPayloadCodecV1.Encode(command);
        var receiptBytes = ProviderEffectAuthorityPayloadCodecV1.Encode(receipt);
        Assert.True(ProviderEffectAuthorityPayloadCodecV1.TryDecodeProviderEffectCommand(commandBytes, out var decodedCommand));
        Assert.True(ProviderEffectAuthorityPayloadCodecV1.TryDecodeProviderEffectReceipt(receiptBytes, out var decodedReceipt));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCommand!.Body);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedReceipt!.Body);
        Assert.Equal(commandBytes, ProviderEffectAuthorityPayloadCodecV1.Encode(decodedCommand));
        Assert.Equal(receiptBytes, ProviderEffectAuthorityPayloadCodecV1.Encode(decodedReceipt));
        Assert.NotEqual(ProviderEffectAuthorityPayloadCodecV1.ComputeHash(command), ProviderEffectAuthorityPayloadCodecV1.ComputeHash(receipt));
    }

    [Fact]
    public void Provider_effect_payloads_reject_invalid_authority_and_body_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var otherAuthority = ExpectedAuthorityVectorV1.Create(other, []);

        Assert.Throws<ArgumentException>(() => new ProviderEffectCommandV1(session, otherAuthority, []));
        Assert.Throws<ArgumentException>(() => new ProviderEffectReceiptV1(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Provider_effect_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = ProviderEffectAuthorityPayloadCodecV1.Encode(new ProviderEffectCommandV1(session, authority, [7]));
        Assert.False(ProviderEffectAuthorityPayloadCodecV1.TryDecodeProviderEffectCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));

        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(ProviderEffectAuthorityPayloadCodecV1.TryDecodeProviderEffectCommand(reordered.Encode(), out _));
        Assert.False(ProviderEffectAuthorityPayloadCodecV1.TryDecodeProviderEffectReceipt(new byte[66_561], out _));
    }

    [Fact]
    public void Provider_effect_registrations_join_the_generated_owner_and_validate_the_outer_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var command = ProviderEffectAuthorityPayloadCodecV1.Encode(new ProviderEffectCommandV1(session, authority, []));
        var receipt = ProviderEffectAuthorityPayloadCodecV1.Encode(new ProviderEffectReceiptV1(session, authority, [8]));

        Assert.Equal((ushort)13, ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectCommandDiscriminator);
        Assert.Equal((ushort)14, ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectReceiptDiscriminator);
        Assert.Equal(OwnerSliceId.S5, ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectCommand.Owner);
        Assert.Equal(OwnerSliceId.S5, ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectReceipt.Owner);
        Assert.True(ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectCommand.Validate(command, session));
        Assert.True(ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectReceipt.Validate(receipt, session));
        Assert.False(ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectCommand.Validate(command, other));
        Assert.False(ProviderEffectAuthorityPayloadRegistrationsV1.ProviderEffectReceipt.Validate(receipt, other));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    {
        var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2));
        return (session, ExpectedAuthorityVectorV1.Create(session, []));
    }

    private static T Id<T>(byte value) where T : struct
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Fill(value);
        var stable = StableId128.FromBytes(bytes);
        return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) :
            typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) :
            throw new InvalidOperationException();
    }
}
