namespace HPD.Agent;

/// <summary>
/// Identifies an agent capability independently from its model-facing function name.
/// </summary>
/// <param name="Value">The stable, non-empty capability identifier.</param>
public readonly record struct CapabilityId(string Value)
{
    /// <summary>
    /// Creates and validates a capability identifier.
    /// </summary>
    /// <param name="value">The stable identifier value.</param>
    /// <returns>A validated capability identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
    public static CapabilityId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new CapabilityId(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
