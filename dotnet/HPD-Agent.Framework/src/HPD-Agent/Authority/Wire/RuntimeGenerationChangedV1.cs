using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal sealed record RuntimeGenerationChangedV1
{
    internal RuntimeGenerationChangedV1(SessionAuthorityStampV1 session, RuntimeGenerationId expectedPrevious,
        RuntimeGenerationId proposedNext, OwnerSliceId owner)
    {
        if (!session.IsValid || !expectedPrevious.IsValid || !proposedNext.IsValid ||
            expectedPrevious == proposedNext || owner != OwnerSliceId.S1)
            throw new ArgumentException("Invalid runtime generation transition.");
        Session = session;
        ExpectedPrevious = expectedPrevious;
        ProposedNext = proposedNext;
        Owner = owner;
    }

    internal SessionAuthorityStampV1 Session { get; }
    internal RuntimeGenerationId ExpectedPrevious { get; }
    internal RuntimeGenerationId ProposedNext { get; }
    internal OwnerSliceId Owner { get; }
}

internal static class RuntimeGenerationChangedCodecV1
{
    private const string SchemaId = "hpd.runtime-generation-changed.v1";

    internal static byte[] Encode(RuntimeGenerationChangedV1 value)
    {
        Span<byte> previous = stackalloc byte[16];
        Span<byte> next = stackalloc byte[16];
        if (!value.ExpectedPrevious.TryWriteBytes(previous) || !value.ProposedNext.TryWriteBytes(next))
            throw new ArgumentException("Both runtime generations are required.", nameof(value));
        return AuthorityGenerationTransitionCodecV1.Encode(value.Session, AuthorityAxisId.Runtime,
            StableId128.FromBytes(previous), StableId128.FromBytes(next));
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out RuntimeGenerationChangedV1? value)
    {
        value = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1) return false;
            var session = SessionAuthorityStampV1Codec.Read(reader);
            if (AuthorityGenerationTransitionCodecV1.Decode(
                    AuthorityGenerationTransitionCodecV1.SchemaFor(AuthorityAxisId.Runtime), OwnerSliceId.S1,
                    session, encoded, out var decoded) != AuthorityGenerationTransitionDecodeV1.Valid)
                return false;
            var candidate = new RuntimeGenerationChangedV1(decoded.Session,
                RuntimeGenerationId.FromValue(decoded.ExpectedPrevious), RuntimeGenerationId.FromValue(decoded.ProposedNext), decoded.Owner);
            if (!Encode(candidate).AsSpan().SequenceEqual(encoded.Span)) return false;
            value = candidate;
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    internal static Hash256 ComputeHash(RuntimeGenerationChangedV1 value) =>
        AuthorityIntegrityHashV1.Compute(SchemaId, 1, 0, Encode(value));
}
