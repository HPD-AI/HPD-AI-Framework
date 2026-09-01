using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Owns the canonical provider-independent checksum for activation control state.</summary>
public static class BaseActivationControlChecksumContract
{
    private const string Domain = "base.activation.control.v3\0";
    private const string YieldLimitFailure = "base.activation.yieldLimitExceeded";

    /// <summary>Creates the canonical checksum for one exact activation control state.</summary>
    /// <param name="activationId">The stable activation identity.</param>
    /// <param name="generation">The positive control generation.</param>
    /// <param name="state">The exact durable activation state.</param>
    /// <param name="effectiveDueAt">The current effective due instant as Unix milliseconds.</param>
    /// <param name="yieldCount">The number of committed durable yields.</param>
    /// <param name="maximumYields">The immutable pinned yield maximum.</param>
    /// <param name="executionSliceOrdinal">The current execution-slice ordinal.</param>
    /// <param name="attemptStartedAt">The accepted logical-attempt start, when claimed at least once.</param>
    /// <param name="sliceStartedAt">The accepted current-slice start, when claimed at least once.</param>
    /// <param name="terminalYieldDisposition">The terminal yield disposition, when yield exhaustion ended the activation.</param>
    /// <param name="terminalYieldFailureCode">The fixed safe terminal yield failure code, when present.</param>
    /// <returns>The immutable 32-byte SHA-256 checksum.</returns>
    public static ImmutableArray<byte> Create(
        string activationId,
        long generation,
        BaseActivationState state,
        long effectiveDueAt,
        long yieldCount,
        long maximumYields,
        long executionSliceOrdinal,
        long? attemptStartedAt,
        long? sliceStartedAt,
        BaseActivationYieldDisposition? terminalYieldDisposition,
        string? terminalYieldFailureCode)
    {
        Validate(activationId, generation, state, effectiveDueAt, yieldCount, maximumYields,
            executionSliceOrdinal, attemptStartedAt, sliceStartedAt, terminalYieldDisposition,
            terminalYieldFailureCode);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(Domain));
        AppendText(hash, activationId);
        AppendInt64(hash, generation);
        AppendInt32(hash, (int)state);
        AppendInt64(hash, effectiveDueAt);
        AppendInt64(hash, yieldCount);
        AppendInt64(hash, maximumYields);
        AppendInt64(hash, executionSliceOrdinal);
        AppendOptionalInt64(hash, attemptStartedAt);
        AppendOptionalInt64(hash, sliceStartedAt);
        hash.AppendData([(byte)(terminalYieldDisposition.HasValue ? 1 : 0)]);
        if (terminalYieldDisposition.HasValue)
            AppendInt32(hash, (int)terminalYieldDisposition.Value);
        hash.AppendData([(byte)(terminalYieldFailureCode is null ? 0 : 1)]);
        if (terminalYieldFailureCode is not null)
            AppendText(hash, terminalYieldFailureCode);
        return hash.GetHashAndReset().ToImmutableArray();
    }

    /// <summary>Validates one untrusted checksum against canonical control authority.</summary>
    /// <param name="checksum">The untrusted checksum.</param>
    /// <param name="activationId">The expected activation identity.</param>
    /// <param name="generation">The expected control generation.</param>
    /// <param name="state">The expected durable activation state.</param>
    /// <param name="effectiveDueAt">The expected effective due instant.</param>
    /// <param name="yieldCount">The expected durable-yield count.</param>
    /// <param name="maximumYields">The expected immutable yield maximum.</param>
    /// <param name="executionSliceOrdinal">The expected execution-slice ordinal.</param>
    /// <param name="attemptStartedAt">The expected logical-attempt start.</param>
    /// <param name="sliceStartedAt">The expected current-slice start.</param>
    /// <param name="terminalYieldDisposition">The expected terminal yield disposition.</param>
    /// <param name="terminalYieldFailureCode">The expected terminal yield failure code.</param>
    /// <returns><see langword="true"/> only for valid authority and an exact fixed-time match.</returns>
    public static bool Matches(
        ReadOnlySpan<byte> checksum,
        string activationId,
        long generation,
        BaseActivationState state,
        long effectiveDueAt,
        long yieldCount,
        long maximumYields,
        long executionSliceOrdinal,
        long? attemptStartedAt,
        long? sliceStartedAt,
        BaseActivationYieldDisposition? terminalYieldDisposition,
        string? terminalYieldFailureCode)
    {
        if (checksum.Length != SHA256.HashSizeInBytes)
            return false;
        try
        {
            ImmutableArray<byte> expected = Create(activationId, generation, state, effectiveDueAt,
                yieldCount, maximumYields, executionSliceOrdinal, attemptStartedAt, sliceStartedAt,
                terminalYieldDisposition, terminalYieldFailureCode);
            return CryptographicOperations.FixedTimeEquals(checksum, expected.AsSpan());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void Validate(
        string activationId,
        long generation,
        BaseActivationState state,
        long effectiveDueAt,
        long yieldCount,
        long maximumYields,
        long executionSliceOrdinal,
        long? attemptStartedAt,
        long? sliceStartedAt,
        BaseActivationYieldDisposition? terminalYieldDisposition,
        string? terminalYieldFailureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        if (generation < 1) throw new ArgumentOutOfRangeException(nameof(generation));
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (effectiveDueAt < 0) throw new ArgumentOutOfRangeException(nameof(effectiveDueAt));
        if (maximumYields is < 0 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(maximumYields));
        if (yieldCount < 0 || yieldCount > maximumYields) throw new ArgumentOutOfRangeException(nameof(yieldCount));
        if (executionSliceOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(executionSliceOrdinal));
        if (attemptStartedAt.HasValue != sliceStartedAt.HasValue
            || executionSliceOrdinal == 0 && attemptStartedAt.HasValue
            || executionSliceOrdinal > 0 && !attemptStartedAt.HasValue
            || attemptStartedAt is < 0 || sliceStartedAt is < 0
            || attemptStartedAt > sliceStartedAt)
            throw new ArgumentException("Activation start authority is inconsistent.");
        if (state == BaseActivationState.YieldPending && (yieldCount < 1 || maximumYields < 1))
            throw new ArgumentException("Yield-pending authority requires a committed yield.");
        bool terminalYield = terminalYieldDisposition == BaseActivationYieldDisposition.LimitExceeded
            && terminalYieldFailureCode == YieldLimitFailure && state == BaseActivationState.Exhausted
            && maximumYields > 0 && yieldCount == maximumYields;
        if (!terminalYield && (terminalYieldDisposition.HasValue || terminalYieldFailureCode is not null))
            throw new ArgumentException("Terminal yield authority is inconsistent.");
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendOptionalInt64(IncrementalHash hash, long? value)
    {
        hash.AppendData([(byte)(value.HasValue ? 1 : 0)]);
        if (value.HasValue) AppendInt64(hash, value.Value);
    }
}
