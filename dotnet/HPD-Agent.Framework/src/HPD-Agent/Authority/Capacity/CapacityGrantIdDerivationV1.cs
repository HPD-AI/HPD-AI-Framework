using System.Security.Cryptography;

namespace HPD.Agent.Authority;

internal static class CapacityGrantIdDerivationV1
{
    private static ReadOnlySpan<byte> Domain => "hpd-capacity-grant-id-v1\0"u8;

    internal static CapacityGrantId Derive(OperationId operationId)
    {
        if (!operationId.IsValid)
            throw new ArgumentException("The operation identity is invalid.", nameof(operationId));

        Span<byte> input = stackalloc byte[Domain.Length + 16];
        Domain.CopyTo(input);
        if (!operationId.TryWriteBytes(input[Domain.Length..]))
            throw new ArgumentException("The operation identity is invalid.", nameof(operationId));

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);
        Span<byte> value = digest[..16];
        if (value.IndexOfAnyExcept((byte)0) < 0)
            value[^1] = 1;
        return CapacityGrantId.FromValue(StableId128.FromBytes(value));
    }
}
