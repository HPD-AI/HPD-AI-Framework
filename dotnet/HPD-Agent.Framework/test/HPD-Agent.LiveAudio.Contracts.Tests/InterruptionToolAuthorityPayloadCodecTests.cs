using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class InterruptionToolAuthorityPayloadCodecTests
{
    [Fact]
    public void Registered_s7_payloads_round_trip_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var interruption = new InterruptionCommand(session, authority, source);
        var settled = new InterruptionSettled(session, authority, source);
        var continuation = new ToolContinuation(session, authority, source);
        var receipt = new ToolEffectReceipt(session, authority, source);
        source[0] = 99;
        var interruptionBytes = InterruptionToolAuthorityPayloadCodec.Encode(interruption);
        var settledBytes = InterruptionToolAuthorityPayloadCodec.Encode(settled);
        var continuationBytes = InterruptionToolAuthorityPayloadCodec.Encode(continuation);
        var receiptBytes = InterruptionToolAuthorityPayloadCodec.Encode(receipt);
        Assert.True(InterruptionToolAuthorityPayloadCodec.TryDecodeInterruptionCommand(interruptionBytes, out var decodedInterruption));
        Assert.True(InterruptionToolAuthorityPayloadCodec.TryDecodeInterruptionSettled(settledBytes, out var decodedSettled));
        Assert.True(InterruptionToolAuthorityPayloadCodec.TryDecodeToolContinuation(continuationBytes, out var decodedContinuation));
        Assert.True(InterruptionToolAuthorityPayloadCodec.TryDecodeToolEffectReceipt(receiptBytes, out var decodedReceipt));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedInterruption!.Body);
        Assert.Equal(interruptionBytes, InterruptionToolAuthorityPayloadCodec.Encode(decodedInterruption));
        Assert.Equal(settledBytes, InterruptionToolAuthorityPayloadCodec.Encode(decodedSettled!));
        Assert.Equal(continuationBytes, InterruptionToolAuthorityPayloadCodec.Encode(decodedContinuation!));
        Assert.Equal(receiptBytes, InterruptionToolAuthorityPayloadCodec.Encode(decodedReceipt!));
        Assert.Equal(4, new[]
        {
            InterruptionToolAuthorityPayloadCodec.ComputeHash(interruption), InterruptionToolAuthorityPayloadCodec.ComputeHash(settled),
            InterruptionToolAuthorityPayloadCodec.ComputeHash(continuation), InterruptionToolAuthorityPayloadCodec.ComputeHash(receipt),
        }.Distinct().Count());
    }

    [Fact]
    public void S7_payloads_reject_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var otherAuthority = ExpectedAuthorityVectorV1.Create(other, []);
        Assert.Throws<ArgumentException>(() => new InterruptionCommand(session, otherAuthority, []));
        Assert.Throws<ArgumentException>(() => new InterruptionSettled(session, authority, new byte[65_537]));
        Assert.Throws<ArgumentException>(() => new ToolContinuation(session, authority, new byte[65_537]));
        Assert.Throws<ArgumentException>(() => new ToolEffectReceipt(session, authority, new byte[65_537]));
    }

    [Fact]
    public void S7_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = InterruptionToolAuthorityPayloadCodec.Encode(new InterruptionCommand(session, authority, [7]));
        Assert.False(InterruptionToolAuthorityPayloadCodec.TryDecodeInterruptionCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(InterruptionToolAuthorityPayloadCodec.TryDecodeToolContinuation(reordered.Encode(), out _));
        Assert.False(InterruptionToolAuthorityPayloadCodec.TryDecodeToolEffectReceipt(new byte[66_561], out _));
    }

    [Fact]
    public void S7_registrations_join_discriminators_owner_and_outer_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var payloads = new[]
        {
            InterruptionToolAuthorityPayloadCodec.Encode(new InterruptionCommand(session, authority, [])),
            InterruptionToolAuthorityPayloadCodec.Encode(new InterruptionSettled(session, authority, [])),
            InterruptionToolAuthorityPayloadCodec.Encode(new ToolContinuation(session, authority, [])),
            InterruptionToolAuthorityPayloadCodec.Encode(new ToolEffectReceipt(session, authority, [])),
        };
        var registrations = new[]
        {
            InterruptionToolAuthorityPayloadRegistrations.InterruptionCommand,
            InterruptionToolAuthorityPayloadRegistrations.InterruptionSettled,
            InterruptionToolAuthorityPayloadRegistrations.ToolContinuation,
            InterruptionToolAuthorityPayloadRegistrations.ToolEffectReceipt,
        };
        Assert.Equal(new ushort[] { 19, 20, 21, 22 }, new[]
        {
            InterruptionToolAuthorityPayloadRegistrations.InterruptionCommandDiscriminator,
            InterruptionToolAuthorityPayloadRegistrations.InterruptionSettledDiscriminator,
            InterruptionToolAuthorityPayloadRegistrations.ToolContinuationDiscriminator,
            InterruptionToolAuthorityPayloadRegistrations.ToolEffectReceiptDiscriminator,
        });
        for (var index = 0; index < registrations.Length; index++)
        {
            Assert.Equal(OwnerSliceId.S7, registrations[index].Owner);
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
