using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class RuntimeGenerationChangedV1Tests
{
    [Fact]
    public void Typed_runtime_transition_round_trips_and_binds_schema_hash()
    {
        var value = new RuntimeGenerationChangedV1(Session(), RuntimeGenerationId.FromValue(Id(1)), RuntimeGenerationId.FromValue(Id(2)), OwnerSliceId.S1);
        var bytes = RuntimeGenerationChangedCodecV1.Encode(value);
        Assert.True(RuntimeGenerationChangedCodecV1.TryDecode(bytes, out var decoded));
        Assert.Equal(value, decoded);
        Assert.Equal(RuntimeGenerationChangedCodecV1.ComputeHash(value), RuntimeGenerationChangedCodecV1.ComputeHash(decoded!));
    }

    [Fact]
    public void Constructor_and_decoder_reject_invalid_or_noncanonical_values()
    {
        var session = Session(); var generation = RuntimeGenerationId.FromValue(Id(1));
        Assert.Throws<ArgumentException>(() => new RuntimeGenerationChangedV1(session, generation, generation, OwnerSliceId.S1));
        Assert.Throws<ArgumentException>(() => new RuntimeGenerationChangedV1(session, generation, RuntimeGenerationId.FromValue(Id(2)), OwnerSliceId.S2));
        Assert.False(RuntimeGenerationChangedCodecV1.TryDecode(new byte[] { 0xff }, out _));
        var valid = RuntimeGenerationChangedCodecV1.Encode(new(session, generation, RuntimeGenerationId.FromValue(Id(2)), OwnerSliceId.S1));
        Assert.False(RuntimeGenerationChangedCodecV1.TryDecode(valid.Concat(new byte[] { 0 }).ToArray(), out _));
    }

    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.FromValue(Id(20)), LiveSessionId.FromValue(Id(21)));
    private static StableId128 Id(byte seed) { Span<byte> bytes = stackalloc byte[16]; bytes.Fill(seed); return StableId128.FromBytes(bytes); }
}
