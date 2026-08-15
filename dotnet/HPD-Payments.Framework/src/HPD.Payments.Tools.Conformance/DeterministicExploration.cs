using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Derives an exact 256-bit cell seed from a caller-owned 256-bit run root.</summary>
internal static class ProofSeed
{
    /// <summary>Derives SHA-256(root || length-prefixed canonical cell dimensions).</summary>
    internal static byte[] Derive(ReadOnlySpan<byte> rootSeed, ProofCellKey cell)
    {
        if (rootSeed.Length != 32) throw new ArgumentException("A proof root seed must contain exactly 256 bits.", nameof(rootSeed));
        ArgumentNullException.ThrowIfNull(cell);
        var text = Encoding.UTF8.GetBytes(cell.ToCanonicalText());
        var material = new byte[36 + text.Length];
        rootSeed.CopyTo(material);
        BinaryPrimitives.WriteInt32BigEndian(material.AsSpan(32, 4), text.Length);
        text.CopyTo(material, 36);
        return SHA256.HashData(material);
    }
}

/// <summary>Produces reproducible bounded schedules without ambient randomness.</summary>
internal static class DeterministicSchedule
{
    /// <summary>Returns a deterministic permutation of <paramref name="count"/> logical actions.</summary>
    internal static int[] Permute(int count, ulong seed)
    {
        if (count is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(count));
        var result = Enumerable.Range(0, count).ToArray();
        var state = seed;
        for (var i = result.Length - 1; i > 0; i--)
        {
            var index = (int)(Next(ref state) % (uint)(i + 1));
            (result[i], result[index]) = (result[index], result[i]);
        }
        return result;
    }

    /// <summary>Returns deterministic deletion candidates ordered from largest reduction to smallest.</summary>
    internal static IReadOnlyList<int[]> Shrink(ReadOnlySpan<int> failingSchedule)
    {
        if (failingSchedule.Length is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(failingSchedule));
        var candidates = new List<int[]>();
        for (var width = HighestPowerOfTwoAtMost(failingSchedule.Length); width >= 1; width /= 2)
        {
            for (var start = 0; start + width <= failingSchedule.Length; start += width)
            {
                if (width == failingSchedule.Length) continue;
                var candidate = new int[failingSchedule.Length - width];
                failingSchedule[..start].CopyTo(candidate);
                failingSchedule[(start + width)..].CopyTo(candidate.AsSpan(start));
                candidates.Add(candidate);
            }
        }
        return candidates.AsReadOnly();
    }

    private static int HighestPowerOfTwoAtMost(int value)
    {
        var result = 1;
        while (result <= value / 2) result *= 2;
        return result;
    }

    private static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

/// <summary>Provides explicit monotone UTC time for deterministic conformance histories.</summary>
internal sealed class ConformanceTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    /// <summary>Creates a provider at an explicit UTC instant.</summary>
    internal ConformanceTimeProvider(DateTimeOffset utcNow)
    {
        if (utcNow.Offset != TimeSpan.Zero) throw new ArgumentException("Conformance time must be UTC.", nameof(utcNow));
        _utcNow = utcNow;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Advances time monotonically by a bounded positive duration.</summary>
    internal void Advance(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(366)) throw new ArgumentOutOfRangeException(nameof(duration));
        _utcNow += duration;
    }
}

/// <summary>Generates bounded deterministic byte corpora and owns every generated case.</summary>
internal static class BoundedCorpus
{
    /// <summary>Generates exact reproducible single-byte mutations of an owned seed input.</summary>
    internal static IReadOnlyList<byte[]> Generate(ReadOnlySpan<byte> input, int caseCount, ulong rootSeed)
    {
        if (input.Length is < 1 or > 1_048_576) throw new ArgumentOutOfRangeException(nameof(input));
        if (caseCount is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(caseCount));
        var state = rootSeed;
        var cases = new byte[caseCount][];
        for (var i = 0; i < cases.Length; i++)
        {
            var owned = input.ToArray();
            var position = (int)(Next(ref state) % (uint)owned.Length);
            var mask = (byte)(1u << (int)(Next(ref state) % 8));
            owned[position] ^= mask;
            cases[i] = owned;
        }
        return Array.AsReadOnly(cases);
    }

    private static ulong Next(ref ulong state)
    {
        state ^= state >> 12; state ^= state << 25; state ^= state >> 27;
        return state * 0x2545F4914F6CDD1DUL;
    }
}
