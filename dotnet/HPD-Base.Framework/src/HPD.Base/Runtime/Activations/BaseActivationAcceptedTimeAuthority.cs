using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class BaseActivationAcceptedTimeAuthority(TimeProvider timeProvider)
{
    private long _sequence;

    internal BaseAcceptedTimeReceipt Capture(string applicationId)
    {
        long utc = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        long monotonic = timeProvider.GetTimestamp();
        long sequence = checked(Interlocked.Increment(ref _sequence));
        const long generation = 1;
        const long maximumForwardSkewMilliseconds = 30_000;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "base.activation.acceptedTime.v2\0");
        Append(hash, applicationId);
        Append(hash, generation);
        Append(hash, utc);
        Append(hash, monotonic);
        Append(hash, sequence);
        Append(hash, maximumForwardSkewMilliseconds);
        return new BaseAcceptedTimeReceipt(
            applicationId,
            generation,
            utc,
            monotonic,
            sequence,
            maximumForwardSkewMilliseconds,
            hash.GetHashAndReset());
    }

    internal static bool Verify(BaseAcceptedTimeReceipt receipt, long nativeUtc)
    {
        if (receipt.ClockGeneration != 1 || receipt.CaptureSequence <= 0 || receipt.CapturedUtc < 0 ||
            receipt.MaximumForwardSkewMilliseconds != 30_000 || string.IsNullOrWhiteSpace(receipt.ApplicationId) ||
            receipt.CapturedUtc > checked(nativeUtc + receipt.MaximumForwardSkewMilliseconds))
            return false;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "base.activation.acceptedTime.v2\0"); Append(hash, receipt.ApplicationId);
        Append(hash, receipt.ClockGeneration); Append(hash, receipt.CapturedUtc); Append(hash, receipt.MonotonicTimestamp);
        Append(hash, receipt.CaptureSequence); Append(hash, receipt.MaximumForwardSkewMilliseconds);
        return CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), receipt.Checksum.Span);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length));
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void Append(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
