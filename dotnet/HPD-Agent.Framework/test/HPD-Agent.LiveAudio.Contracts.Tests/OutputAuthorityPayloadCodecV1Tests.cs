using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class OutputAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Registered_output_payloads_round_trip_canonically_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority();
        byte[] source = [1, 2, 3, 4];
        var command = new OutputSinkCommandV1(session, authority, source);
        var receipt = new OutputSinkReceiptV1(session, authority, source);
        var heard = new HeardRangeFactV1(session, authority, source);
        source[0] = 99;

        var commandBytes = OutputAuthorityPayloadCodecV1.Encode(command);
        var receiptBytes = OutputAuthorityPayloadCodecV1.Encode(receipt);
        var heardBytes = OutputAuthorityPayloadCodecV1.Encode(heard);
        Assert.True(OutputAuthorityPayloadCodecV1.TryDecodeOutputSinkCommand(commandBytes, out var decodedCommand));
        Assert.True(OutputAuthorityPayloadCodecV1.TryDecodeOutputSinkReceipt(receiptBytes, out var decodedReceipt));
        Assert.True(OutputAuthorityPayloadCodecV1.TryDecodeHeardRangeFact(heardBytes, out var decodedHeard));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCommand!.Body);
        Assert.Equal(commandBytes, OutputAuthorityPayloadCodecV1.Encode(decodedCommand));
        Assert.Equal(receiptBytes, OutputAuthorityPayloadCodecV1.Encode(decodedReceipt!));
        Assert.Equal(heardBytes, OutputAuthorityPayloadCodecV1.Encode(decodedHeard!));
        Assert.Equal(3, new[]
        {
            OutputAuthorityPayloadCodecV1.ComputeHash(command),
            OutputAuthorityPayloadCodecV1.ComputeHash(receipt),
            OutputAuthorityPayloadCodecV1.ComputeHash(heard),
        }.Distinct().Count());
    }

    [Fact]
    public void Output_payloads_reject_invalid_authority_and_body_bounds()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var otherAuthority = ExpectedAuthorityVectorV1.Create(other, []);
        Assert.Throws<ArgumentException>(() => new OutputSinkCommandV1(session, otherAuthority, []));
        Assert.Throws<ArgumentException>(() => new OutputSinkReceiptV1(session, authority, new byte[65_537]));
        Assert.Throws<ArgumentException>(() => new HeardRangeFactV1(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Output_payload_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority();
        var canonical = OutputAuthorityPayloadCodecV1.Encode(new OutputSinkCommandV1(session, authority, [7]));
        Assert.False(OutputAuthorityPayloadCodecV1.TryDecodeOutputSinkCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax);
        reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]);
        reordered.WriteEndMap();
        Assert.False(OutputAuthorityPayloadCodecV1.TryDecodeOutputSinkReceipt(reordered.Encode(), out _));
        Assert.False(OutputAuthorityPayloadCodecV1.TryDecodeHeardRangeFact(new byte[66_561], out _));
    }

    [Fact]
    public void Output_payload_registrations_join_generated_discriminators_owner_and_session()
    {
        var (session, authority) = Authority();
        var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var command = OutputAuthorityPayloadCodecV1.Encode(new OutputSinkCommandV1(session, authority, []));
        var receipt = OutputAuthorityPayloadCodecV1.Encode(new OutputSinkReceiptV1(session, authority, [8]));
        var heard = OutputAuthorityPayloadCodecV1.Encode(new HeardRangeFactV1(session, authority, [9]));
        Assert.Equal((ushort)16, OutputAuthorityPayloadRegistrationsV1.OutputSinkCommandDiscriminator);
        Assert.Equal((ushort)17, OutputAuthorityPayloadRegistrationsV1.OutputSinkReceiptDiscriminator);
        Assert.Equal((ushort)18, OutputAuthorityPayloadRegistrationsV1.HeardRangeFactDiscriminator);
        Assert.All(new[] { OutputAuthorityPayloadRegistrationsV1.OutputSinkCommand, OutputAuthorityPayloadRegistrationsV1.OutputSinkReceipt, OutputAuthorityPayloadRegistrationsV1.HeardRangeFact }, registration => Assert.Equal(OwnerSliceId.S6, registration.Owner));
        Assert.True(OutputAuthorityPayloadRegistrationsV1.OutputSinkCommand.Validate(command, session));
        Assert.True(OutputAuthorityPayloadRegistrationsV1.OutputSinkReceipt.Validate(receipt, session));
        Assert.True(OutputAuthorityPayloadRegistrationsV1.HeardRangeFact.Validate(heard, session));
        Assert.False(OutputAuthorityPayloadRegistrationsV1.OutputSinkCommand.Validate(command, other));
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
