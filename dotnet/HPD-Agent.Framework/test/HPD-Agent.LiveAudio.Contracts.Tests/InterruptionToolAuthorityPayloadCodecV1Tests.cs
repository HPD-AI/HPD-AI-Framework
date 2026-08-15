using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class InterruptionToolAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Registered_s7_payloads_round_trip_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var interruption = new InterruptionCommandV1(session, authority, source);
        var settled = new InterruptionSettledV1(session, authority, source);
        var continuation = new ToolContinuationV1(session, authority, source);
        var receipt = new ToolEffectReceiptV1(session, authority, source);
        source[0] = 99;
        var interruptionBytes = InterruptionToolAuthorityPayloadCodecV1.Encode(interruption);
        var settledBytes = InterruptionToolAuthorityPayloadCodecV1.Encode(settled);
        var continuationBytes = InterruptionToolAuthorityPayloadCodecV1.Encode(continuation);
        var receiptBytes = InterruptionToolAuthorityPayloadCodecV1.Encode(receipt);
        Assert.True(InterruptionToolAuthorityPayloadCodecV1.TryDecodeInterruptionCommand(interruptionBytes, out var decodedInterruption));
        Assert.True(InterruptionToolAuthorityPayloadCodecV1.TryDecodeInterruptionSettled(settledBytes, out var decodedSettled));
        Assert.True(InterruptionToolAuthorityPayloadCodecV1.TryDecodeToolContinuation(continuationBytes, out var decodedContinuation));
        Assert.True(InterruptionToolAuthorityPayloadCodecV1.TryDecodeToolEffectReceipt(receiptBytes, out var decodedReceipt));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedInterruption!.Body);
        Assert.Equal(interruptionBytes, InterruptionToolAuthorityPayloadCodecV1.Encode(decodedInterruption));
        Assert.Equal(settledBytes, InterruptionToolAuthorityPayloadCodecV1.Encode(decodedSettled!));
        Assert.Equal(continuationBytes, InterruptionToolAuthorityPayloadCodecV1.Encode(decodedContinuation!));
        Assert.Equal(receiptBytes, InterruptionToolAuthorityPayloadCodecV1.Encode(decodedReceipt!));
        Assert.Equal(4, new[]
        {
            InterruptionToolAuthorityPayloadCodecV1.ComputeHash(interruption), InterruptionToolAuthorityPayloadCodecV1.ComputeHash(settled),
            InterruptionToolAuthorityPayloadCodecV1.ComputeHash(continuation), InterruptionToolAuthorityPayloadCodecV1.ComputeHash(receipt),
        }.Distinct().Count());
    }

    [Fact]
    public void S7_payloads_reject_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var otherAuthority = ExpectedAuthorityVectorV1.Create(other, []);
        Assert.Throws<ArgumentException>(() => new InterruptionCommandV1(session, otherAuthority, []));
        Assert.Throws<ArgumentException>(() => new InterruptionSettledV1(session, authority, new byte[65_537]));
        Assert.Throws<ArgumentException>(() => new ToolContinuationV1(session, authority, new byte[65_537]));
        Assert.Throws<ArgumentException>(() => new ToolEffectReceiptV1(session, authority, new byte[65_537]));
    }

    [Fact]
    public void S7_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = InterruptionToolAuthorityPayloadCodecV1.Encode(new InterruptionCommandV1(session, authority, [7]));
        Assert.False(InterruptionToolAuthorityPayloadCodecV1.TryDecodeInterruptionCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(InterruptionToolAuthorityPayloadCodecV1.TryDecodeToolContinuation(reordered.Encode(), out _));
        Assert.False(InterruptionToolAuthorityPayloadCodecV1.TryDecodeToolEffectReceipt(new byte[66_561], out _));
    }

    [Fact]
    public void S7_registrations_join_discriminators_owner_and_outer_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var payloads = new[]
        {
            InterruptionToolAuthorityPayloadCodecV1.Encode(new InterruptionCommandV1(session, authority, [])),
            InterruptionToolAuthorityPayloadCodecV1.Encode(new InterruptionSettledV1(session, authority, [])),
            InterruptionToolAuthorityPayloadCodecV1.Encode(new ToolContinuationV1(session, authority, [])),
            InterruptionToolAuthorityPayloadCodecV1.Encode(new ToolEffectReceiptV1(session, authority, [])),
        };
        var registrations = new[]
        {
            InterruptionToolAuthorityPayloadRegistrationsV1.InterruptionCommand,
            InterruptionToolAuthorityPayloadRegistrationsV1.InterruptionSettled,
            InterruptionToolAuthorityPayloadRegistrationsV1.ToolContinuation,
            InterruptionToolAuthorityPayloadRegistrationsV1.ToolEffectReceipt,
        };
        Assert.Equal(new ushort[] { 19, 20, 21, 22 }, new[]
        {
            InterruptionToolAuthorityPayloadRegistrationsV1.InterruptionCommandDiscriminator,
            InterruptionToolAuthorityPayloadRegistrationsV1.InterruptionSettledDiscriminator,
            InterruptionToolAuthorityPayloadRegistrationsV1.ToolContinuationDiscriminator,
            InterruptionToolAuthorityPayloadRegistrationsV1.ToolEffectReceiptDiscriminator,
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
