using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Measures explicitly scoped same-thread allocation and elapsed time without assigning a budget verdict.</summary>
internal static class ResourceProbe
{
    /// <summary>Warms and measures a synchronous action under an exact iteration count.</summary>
    internal static ResourceObservation Measure(Action action, int warmupIterations, int measuredIterations)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (warmupIterations is < 0 or > 100_000 || measuredIterations is < 1 or > 10_000_000)
            throw new ArgumentOutOfRangeException(nameof(measuredIterations));
        for (var i = 0; i < warmupIterations; i++) action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < measuredIterations; i++) action();
        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        var allocated = checked(GC.GetAllocatedBytesForCurrentThread() - before);
        return new(measuredIterations, allocated, elapsed, Environment.CurrentManagedThreadId,
            $"server={System.Runtime.GCSettings.IsServerGC};latency={System.Runtime.GCSettings.LatencyMode}");
    }
}

/// <summary>Records scoped resource observations; it never implies a pass threshold.</summary>
internal sealed record ResourceObservation(int Iterations, long SameThreadAllocatedBytes, TimeSpan Elapsed,
    int ManagedThreadId, string GCSettings);

/// <summary>Detects a synthetic secret canary in raw UTF-8, hexadecimal, or Base64 form.</summary>
internal sealed class SecretCanary
{
    private readonly byte[] _raw;
    private readonly byte[][] _representations;

    /// <summary>Creates an owned bounded synthetic canary; production secrets are prohibited.</summary>
    internal SecretCanary(ReadOnlySpan<byte> syntheticCanary)
    {
        if (syntheticCanary.Length is < 16 or > 256) throw new ArgumentOutOfRangeException(nameof(syntheticCanary));
        _raw = syntheticCanary.ToArray();
        _representations =
        [
            _raw.ToArray(),
            Encoding.ASCII.GetBytes(Convert.ToHexString(_raw)),
            Encoding.ASCII.GetBytes(Convert.ToHexStringLower(_raw)),
            Encoding.ASCII.GetBytes(Convert.ToBase64String(_raw)),
        ];
    }

    /// <summary>Returns true when any retained canary representation appears in the candidate bytes.</summary>
    internal bool IsExposed(ReadOnlySpan<byte> candidate)
    {
        foreach (var representation in _representations)
            if (candidate.IndexOf(representation) >= 0) return true;
        return false;
    }

    /// <summary>Clears all owned canary bytes.</summary>
    internal void Clear()
    {
        CryptographicOperations.ZeroMemory(_raw);
        foreach (var representation in _representations) CryptographicOperations.ZeroMemory(representation);
    }
}
