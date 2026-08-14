using HPD.Payments.Persistence.AtomicDomains;

namespace HPD.Payments.Adapters.Postgres;

/// <summary>Names the exact transaction outcome observed by the PostgreSQL adapter.</summary>
public enum PostgresTransactionOutcome
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The transaction committed and the scoped postcondition was read back.</summary>
    Committed,
    /// <summary>A generation, epoch, fence, digest, or endpoint guard conflicted.</summary>
    Conflict,
    /// <summary>The requested operation lies outside the certified domain boundary.</summary>
    Unsupported,
    /// <summary>PostgreSQL reported a serialization/deadlock failure and no successful retry was observed.</summary>
    RetryExhausted,
    /// <summary>Connection loss or cancellation crossed a commit boundary, so the durable result is unknown.</summary>
    Indeterminate,
}

/// <summary>Reports one PostgreSQL transaction without extending its atomic boundary.</summary>
public sealed record PostgresTransactionReceipt
{
    /// <summary>Gets the exact requested domain.</summary>
    public AtomicDomain Domain { get; }
    /// <summary>Gets the closed transaction outcome.</summary>
    public PostgresTransactionOutcome Outcome { get; }
    /// <summary>Gets the owner epoch observed under lock, or zero when unavailable.</summary>
    public ulong Epoch { get; }
    /// <summary>Gets the fencing token observed under lock, or zero when unavailable.</summary>
    public ulong Fence { get; }
    /// <summary>Gets a bounded stable diagnostic code.</summary>
    public string Code { get; }

    /// <summary>Creates a bounded PostgreSQL transaction receipt.</summary>
    /// <exception cref="ArgumentException">The domain, outcome, epoch/fence, or code is inconsistent.</exception>
    public PostgresTransactionReceipt(AtomicDomain domain, PostgresTransactionOutcome outcome, ulong epoch, ulong fence, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (!domain.IsValid || outcome == PostgresTransactionOutcome.None || !Enum.IsDefined(outcome) || code.Length is < 1 or > 96 ||
            code.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) || ((epoch == 0) != (fence == 0)))
            throw new ArgumentException("Invalid PostgreSQL transaction receipt.");
        Domain = domain; Outcome = outcome; Epoch = epoch; Fence = fence; Code = code;
    }
}
