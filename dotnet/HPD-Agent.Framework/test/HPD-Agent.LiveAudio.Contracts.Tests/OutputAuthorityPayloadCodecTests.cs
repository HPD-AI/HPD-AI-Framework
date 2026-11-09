using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class OutputAuthorityPayloadCodecTests
{
    [Fact]
    public void Registered_output_payloads_round_trip_canonically_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var command = new OutputSinkCommand(session, authority, source);
        var receipt = new OutputSinkReceipt(session, authority, source);
        var heard = new HeardRangeFact(session, authority, source);
        source[0] = 99;

        var commandBytes = OutputAuthorityPayloadCodec.Encode(command);
        var receiptBytes = OutputAuthorityPayloadCodec.Encode(receipt);
        var heardBytes = OutputAuthorityPayloadCodec.Encode(heard);
        Assert.True(OutputAuthorityPayloadCodec.TryDecodeOutputSinkCommand(commandBytes, out var decodedCommand));
        Assert.True(OutputAuthorityPayloadCodec.TryDecodeOutputSinkReceipt(receiptBytes, out var decodedReceipt));
        Assert.True(OutputAuthorityPayloadCodec.TryDecodeHeardRangeFact(heardBytes, out var decodedHeard));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCommand!.Body);
        Assert.Equal(commandBytes, OutputAuthorityPayloadCodec.Encode(decodedCommand));
        Assert.Equal(receiptBytes, OutputAuthorityPayloadCodec.Encode(decodedReceipt!));
        Assert.Equal(heardBytes, OutputAuthorityPayloadCodec.Encode(decodedHeard!));
        Assert.Equal(3, new[]
        {
            OutputAuthorityPayloadCodec.ComputeHash(command),
            OutputAuthorityPayloadCodec.ComputeHash(receipt),
            OutputAuthorityPayloadCodec.ComputeHash(heard),
        }.Distinct().Count());
    }

    [Fact]
    public void Output_payloads_reject_invalid_authority_and_body_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var otherAuthority = ExpectedAuthorityVectorV1.Create(other, []);
        Assert.Throws<ArgumentException>(() => new OutputSinkCommand(session, otherAuthority, []));
        Assert.Throws<ArgumentException>(() => new OutputSinkReceipt(session, authority, new byte[65_537]));
        Assert.Throws<ArgumentException>(() => new HeardRangeFact(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Output_payload_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = OutputAuthorityPayloadCodec.Encode(new OutputSinkCommand(session, authority, [7]));
        Assert.False(OutputAuthorityPayloadCodec.TryDecodeOutputSinkCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(OutputAuthorityPayloadCodec.TryDecodeOutputSinkReceipt(reordered.Encode(), out _));
        Assert.False(OutputAuthorityPayloadCodec.TryDecodeHeardRangeFact(new byte[66_561], out _));
    }

    [Fact]
    public void Output_payload_registrations_join_generated_discriminators_owner_and_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var command = OutputAuthorityPayloadCodec.Encode(new OutputSinkCommand(session, authority, []));
        var receipt = OutputAuthorityPayloadCodec.Encode(new OutputSinkReceipt(session, authority, [8]));
        var heard = OutputAuthorityPayloadCodec.Encode(new HeardRangeFact(session, authority, [9]));
        Assert.Equal((ushort)16, OutputAuthorityPayloadRegistrations.OutputSinkCommandDiscriminator);
        Assert.Equal((ushort)17, OutputAuthorityPayloadRegistrations.OutputSinkReceiptDiscriminator);
        Assert.Equal((ushort)18, OutputAuthorityPayloadRegistrations.HeardRangeFactDiscriminator);
        Assert.All(new[] { OutputAuthorityPayloadRegistrations.OutputSinkCommand, OutputAuthorityPayloadRegistrations.OutputSinkReceipt, OutputAuthorityPayloadRegistrations.HeardRangeFact }, registration => Assert.Equal(OwnerSliceId.S6, registration.Owner));
        Assert.True(OutputAuthorityPayloadRegistrations.OutputSinkCommand.Validate(command, session));
        Assert.True(OutputAuthorityPayloadRegistrations.OutputSinkReceipt.Validate(receipt, session));
        Assert.True(OutputAuthorityPayloadRegistrations.HeardRangeFact.Validate(heard, session));
        Assert.False(OutputAuthorityPayloadRegistrations.OutputSinkCommand.Validate(command, other));
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
