using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ProviderEffectAuthorityPayloadCodecTests
{
    [Fact]
    public void Registered_provider_effect_payloads_round_trip_canonically_and_own_body_bytes()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var command = new ProviderEffectCommand(session, authority, source);
        var receipt = new ProviderEffectReceipt(session, authority, source);
        source[0] = 99;

        var commandBytes = ProviderEffectAuthorityPayloadCodec.Encode(command);
        var receiptBytes = ProviderEffectAuthorityPayloadCodec.Encode(receipt);
        Assert.True(ProviderEffectAuthorityPayloadCodec.TryDecodeProviderEffectCommand(commandBytes, out var decodedCommand));
        Assert.True(ProviderEffectAuthorityPayloadCodec.TryDecodeProviderEffectReceipt(receiptBytes, out var decodedReceipt));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCommand!.Body);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedReceipt!.Body);
        Assert.Equal(commandBytes, ProviderEffectAuthorityPayloadCodec.Encode(decodedCommand));
        Assert.Equal(receiptBytes, ProviderEffectAuthorityPayloadCodec.Encode(decodedReceipt));
        Assert.NotEqual(ProviderEffectAuthorityPayloadCodec.ComputeHash(command), ProviderEffectAuthorityPayloadCodec.ComputeHash(receipt));
    }

    [Fact]
    public void Provider_effect_payloads_reject_invalid_authority_and_body_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var otherAuthority = ExpectedAuthorityVectorV1.Create(other, []);

        Assert.Throws<ArgumentException>(() => new ProviderEffectCommand(session, otherAuthority, []));
        Assert.Throws<ArgumentException>(() => new ProviderEffectReceipt(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Provider_effect_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = ProviderEffectAuthorityPayloadCodec.Encode(new ProviderEffectCommand(session, authority, [7]));
        Assert.False(ProviderEffectAuthorityPayloadCodec.TryDecodeProviderEffectCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));

        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(ProviderEffectAuthorityPayloadCodec.TryDecodeProviderEffectCommand(reordered.Encode(), out _));
        Assert.False(ProviderEffectAuthorityPayloadCodec.TryDecodeProviderEffectReceipt(new byte[66_561], out _));
    }

    [Fact]
    public void Provider_effect_registrations_join_the_generated_owner_and_validate_the_outer_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var command = ProviderEffectAuthorityPayloadCodec.Encode(new ProviderEffectCommand(session, authority, []));
        var receipt = ProviderEffectAuthorityPayloadCodec.Encode(new ProviderEffectReceipt(session, authority, [8]));

        Assert.Equal((ushort)13, ProviderEffectAuthorityPayloadRegistrations.ProviderEffectCommandDiscriminator);
        Assert.Equal((ushort)14, ProviderEffectAuthorityPayloadRegistrations.ProviderEffectReceiptDiscriminator);
        Assert.Equal(OwnerSliceId.S5, ProviderEffectAuthorityPayloadRegistrations.ProviderEffectCommand.Owner);
        Assert.Equal(OwnerSliceId.S5, ProviderEffectAuthorityPayloadRegistrations.ProviderEffectReceipt.Owner);
        Assert.True(ProviderEffectAuthorityPayloadRegistrations.ProviderEffectCommand.Validate(command, session));
        Assert.True(ProviderEffectAuthorityPayloadRegistrations.ProviderEffectReceipt.Validate(receipt, session));
        Assert.False(ProviderEffectAuthorityPayloadRegistrations.ProviderEffectCommand.Validate(command, other));
        Assert.False(ProviderEffectAuthorityPayloadRegistrations.ProviderEffectReceipt.Validate(receipt, other));
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
