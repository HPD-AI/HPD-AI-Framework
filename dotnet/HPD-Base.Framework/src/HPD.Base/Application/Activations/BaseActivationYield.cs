using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Owns one immutable 256-bit activation-progress fingerprint.</summary>
public sealed class BaseActivationProgressFingerprint
{
    private readonly byte[] _bytes;

    private BaseActivationProgressFingerprint(byte[] bytes) => _bytes = bytes;

    /// <summary>Creates one fingerprint from exactly 32 bytes.</summary>
    /// <param name="bytes">The complete SHA-256 fingerprint bytes.</param>
    /// <returns>A deeply owned fingerprint.</returns>
    /// <exception cref="ArgumentException">The value is not exactly 32 bytes.</exception>
    public static BaseActivationProgressFingerprint Create(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 32)
            throw new ArgumentException("The activation progress fingerprint must contain exactly 32 bytes.", nameof(bytes));
        return new BaseActivationProgressFingerprint(bytes.ToArray());
    }

    /// <summary>Returns a defensive copy of the fingerprint bytes.</summary>
    public byte[] ToArray() => _bytes.ToArray();

    internal ImmutableArray<byte> ToImmutableArray() => _bytes.ToImmutableArray();
}

/// <summary>Requests durable resumption of the same activation after bounded progress.</summary>
public sealed record BaseActivationYield
{
    /// <summary>Gets the optional requested UTC resumption instant; null means accepted-now.</summary>
    public DateTimeOffset? ResumeAt { get; init; }
    /// <summary>Gets the opaque progress fingerprint.</summary>
    public required BaseActivationProgressFingerprint ProgressFingerprint { get; init; }
}
