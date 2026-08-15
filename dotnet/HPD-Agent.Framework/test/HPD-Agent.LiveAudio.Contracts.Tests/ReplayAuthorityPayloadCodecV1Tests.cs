using System.Formats.Cbor;
using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class ReplayAuthorityPayloadCodecV1Tests
{
    [Fact]
    public void Replay_payloads_round_trip_own_bytes_and_separate_hash_domains()
    {
        var (session, authority) = Authority(); byte[] source = [1, 2, 3, 4];
        var command = new ReplayRunCommandV1(session, authority, source); var evidence = new ReplayEvidenceFactV1(session, authority, source); source[0] = 99;
        var commandBytes = ReplayAuthorityPayloadCodecV1.Encode(command); var evidenceBytes = ReplayAuthorityPayloadCodecV1.Encode(evidence);
        Assert.True(ReplayAuthorityPayloadCodecV1.TryDecodeReplayRunCommand(commandBytes, out var decodedCommand));
        Assert.True(ReplayAuthorityPayloadCodecV1.TryDecodeReplayEvidenceFact(evidenceBytes, out var decodedEvidence));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, decodedCommand!.Body);
        Assert.Equal(commandBytes, ReplayAuthorityPayloadCodecV1.Encode(decodedCommand));
        Assert.Equal(evidenceBytes, ReplayAuthorityPayloadCodecV1.Encode(decodedEvidence!));
        Assert.NotEqual(ReplayAuthorityPayloadCodecV1.ComputeHash(command), ReplayAuthorityPayloadCodecV1.ComputeHash(evidence));
    }

    [Fact]
    public void Replay_payloads_reject_invalid_authority_and_bounds()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        Assert.Throws<ArgumentException>(() => new ReplayRunCommandV1(session, ExpectedAuthorityVectorV1.Create(other, []), []));
        Assert.Throws<ArgumentException>(() => new ReplayEvidenceFactV1(session, authority, new byte[65_537]));
    }

    [Fact]
    public void Replay_decoders_fail_closed_for_noncanonical_trailing_and_oversize_data()
    {
        var (session, authority) = Authority(); var canonical = ReplayAuthorityPayloadCodecV1.Encode(new ReplayRunCommandV1(session, authority, [7]));
        Assert.False(ReplayAuthorityPayloadCodecV1.TryDecodeReplayRunCommand(canonical.Concat(new byte[] { 0 }).ToArray(), out _));
        var reordered = new CborWriter(CborConformanceMode.Lax); reordered.WriteStartMap(3);
        reordered.WriteUInt64(2); reordered.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        reordered.WriteUInt64(1); reordered.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(session));
        reordered.WriteUInt64(3); reordered.WriteByteString([7]); reordered.WriteEndMap();
        Assert.False(ReplayAuthorityPayloadCodecV1.TryDecodeReplayEvidenceFact(reordered.Encode(), out _));
        Assert.False(ReplayAuthorityPayloadCodecV1.TryDecodeReplayEvidenceFact(new byte[66_561], out _));
    }

    [Fact]
    public void Replay_registrations_join_discriminators_owner_and_session()
    {
        var (session, authority) = Authority(); var other = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(3), Id<LiveSessionId>(4));
        var command = ReplayAuthorityPayloadCodecV1.Encode(new ReplayRunCommandV1(session, authority, []));
        var evidence = ReplayAuthorityPayloadCodecV1.Encode(new ReplayEvidenceFactV1(session, authority, []));
        Assert.Equal((ushort)29, ReplayAuthorityPayloadRegistrationsV1.ReplayRunCommandDiscriminator);
        Assert.Equal((ushort)30, ReplayAuthorityPayloadRegistrationsV1.ReplayEvidenceFactDiscriminator);
        Assert.Equal(OwnerSliceId.S10, ReplayAuthorityPayloadRegistrationsV1.ReplayRunCommand.Owner);
        Assert.Equal(OwnerSliceId.S10, ReplayAuthorityPayloadRegistrationsV1.ReplayEvidenceFact.Owner);
        Assert.True(ReplayAuthorityPayloadRegistrationsV1.ReplayRunCommand.Validate(command, session));
        Assert.True(ReplayAuthorityPayloadRegistrationsV1.ReplayEvidenceFact.Validate(evidence, session));
        Assert.False(ReplayAuthorityPayloadRegistrationsV1.ReplayRunCommand.Validate(command, other));
    }

    private static (SessionAuthorityStampV1 Session, ExpectedAuthorityVectorV1 Authority) Authority()
    { var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(1), Id<LiveSessionId>(2)); return (session, ExpectedAuthorityVectorV1.Create(session, [])); }
    private static T Id<T>(byte value) where T : struct
    { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(value); var stable = StableId128.FromBytes(bytes); return typeof(T) == typeof(RuntimeGenerationId) ? (T)(object)RuntimeGenerationId.FromValue(stable) : typeof(T) == typeof(LiveSessionId) ? (T)(object)LiveSessionId.FromValue(stable) : throw new InvalidOperationException(); }
}
