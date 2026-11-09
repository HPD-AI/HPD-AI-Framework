using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ReplayAuthorityPayloadCodecTests
{
    [Fact]
    public void Replay_payloads_round_trip_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority(); byte[] source = [1, 2, 3, 4];
        var command = new ReplayRunCommand(session, authority, source); var evidence = new ReplayEvidenceFact(session, authority, source); source[0] = 99;
        var commandBytes = ReplayAuthorityPayloadCodec.Encode(command); var evidenceBytes = ReplayAuthorityPayloadCodec.Encode(evidence);
        Assert.True(ReplayAuthorityPayloadCodec.TryDecodeReplayRunCommand(commandBytes, out var decodedCommand));
        Assert.True(ReplayAuthorityPayloadCodec.TryDecodeReplayEvidenceFact(evidenceBytes, out var decodedEvidence));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCommand!.Body);
        Assert.Equal(commandBytes, ReplayAuthorityPayloadCodec.Encode(decodedCommand));
        Assert.Equal(evidenceBytes, ReplayAuthorityPayloadCodec.Encode(decodedEvidence!));
        Assert.NotEqual(ReplayAuthorityPayloadCodec.ComputeHash(command), ReplayAuthorityPayloadCodec.ComputeHash(evidence));
    }

    [Fact]
    public void Replay_payloads_reject_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new ReplayRunCommand(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new ReplayEvidenceFact(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Replay_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority(); var canonical = ReplayAuthorityPayloadCodec.Encode(new ReplayRunCommand(session, authority, [7]));
        Assert.False(ReplayAuthorityPayloadCodec.TryDecodeReplayRunCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(ReplayAuthorityPayloadCodec.TryDecodeReplayEvidenceFact(reordered.Encode(), out _));
        Assert.False(ReplayAuthorityPayloadCodec.TryDecodeReplayEvidenceFact(new byte[66_561], out _));
    }

    [Fact]
    public void Replay_registrations_join_discriminators_owner_and_session()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var command = ReplayAuthorityPayloadCodec.Encode(new ReplayRunCommand(session, authority, []));
        var evidence = ReplayAuthorityPayloadCodec.Encode(new ReplayEvidenceFact(session, authority, []));
        Assert.Equal((ushort)29, ReplayAuthorityPayloadRegistrations.ReplayRunCommandDiscriminator);
        Assert.Equal((ushort)30, ReplayAuthorityPayloadRegistrations.ReplayEvidenceFactDiscriminator);
        Assert.Equal(OwnerSliceId.S10, ReplayAuthorityPayloadRegistrations.ReplayRunCommand.Owner);
        Assert.Equal(OwnerSliceId.S10, ReplayAuthorityPayloadRegistrations.ReplayEvidenceFact.Owner);
        Assert.True(ReplayAuthorityPayloadRegistrations.ReplayRunCommand.Validate(command, session));
        Assert.True(ReplayAuthorityPayloadRegistrations.ReplayEvidenceFact.Validate(evidence, session));
        Assert.False(ReplayAuthorityPayloadRegistrations.ReplayRunCommand.Validate(command, other));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2)); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }
    private static T Id<T>(byte value) where T : struct
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes); return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) : typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException(); }
}
