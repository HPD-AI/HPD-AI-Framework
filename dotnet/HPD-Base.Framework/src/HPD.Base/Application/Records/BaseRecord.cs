
namespace HPD.Base;

/// <summary>
/// Represents a typed BASE record and its authoritative metadata.
/// </summary>
public sealed record BaseRecord<T>
{
    /// <summary>Gets the stable record identifier.</summary>
    public required RecordId Id { get; init; }

    /// <summary>Gets the policy-projected typed value.</summary>
    public required T Value { get; init; }

    /// <summary>Gets the authoritative revision, when available.</summary>
    public RevisionToken? Revision { get; init; }

    /// <summary>Gets the creation timestamp, when available.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Gets the last-update timestamp, when available.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Gets whether policy redacted the returned payload.</summary>
    public bool Redacted { get; init; }
}
